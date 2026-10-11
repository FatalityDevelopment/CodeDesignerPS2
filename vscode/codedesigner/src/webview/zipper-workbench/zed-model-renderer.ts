import * as THREE from 'three';
import type { ZedModel } from './zed-model-parser';

// Guard rails for the WebGL upload path. PS2 textures are always 2^N square
// and tiny by modern standards, so anything above 2048 is almost certainly a
// mis-parse — uploading it can blow VRAM budgets and crash the renderer
// process. The unique material cap is the same idea: a model that
// "wants" 200 materials is almost certainly a misclassified payload.
const MAX_TEXTURE_DIMENSION = 2048;
const MAX_UNIQUE_MATERIALS = 64;
// Hard cap on the BufferGeometry the renderer will allocate. World models
// stay well under 100k verts; anything above this is parsed garbage from a
// payload that isn't actually a 3D model (UI / script blobs that happen to
// match the chunk magic). Crashing the GPU process with a 200MB typed
// array is exactly the failure mode the user has been hitting.
const MAX_VERTICES_TOTAL = 200_000;
const MAX_INDICES_TOTAL = 600_000;

// Three.js viewer for ZED model payloads. Mirrors the structure of the PS2
// memory-card icon renderer (resize observer, raf loop, dispose) but uses an
// orbit-style mouse drag instead of an animation tween. The scene Y axis is
// inverted at the root so map/world models render right-side-up (the format
// stores them flipped vertically).

interface DragState {
  active: boolean;
  lastX: number;
  lastY: number;
}

export interface ModelTexturePixels {
  width: number;
  height: number;
  rgba: Uint8Array;
  // From the texture's GS CLAMP register: per-axis wrap mode.
  wrapS?: 'repeat' | 'clamp';
  wrapT?: 'repeat' | 'clamp';
  // From the GS TEST register: discard texels below this alpha (0..1).
  alphaTest?: number | null;
  // True when the decoded pixels contain any non-opaque texel.
  hasTransparency?: boolean;
}

export interface ModelTextureMap {
  // Map from lowercased .tif name -> decoded texture pixels.
  [tifName: string]: ModelTexturePixels;
}

export type UvMode = 'direct' | 'flipV' | 'swap' | 'swapFlipV';
export type ModelCameraView = 'auto' | 'iso' | 'top' | 'front' | 'back' | 'left' | 'right' | 'mapTop' | 'mapIso';

export interface ModelBounds3 {
  minX: number; minY: number; minZ: number;
  maxX: number; maxY: number; maxZ: number;
}

export interface ModelRendererOptions {
  wireframe?: boolean;
  flipY?: boolean;
  chunkIndex?: number;
  // UV orientation applied to the vertex UVs before upload. PS2 conventions
  // vary per file family - the reference implementation exposes the same set.
  uvMode?: UvMode;
  // Per-chunk textures keyed by lowercased .tif filename.
  textures?: ModelTextureMap | null;
  // Default texture name (lowercased) for chunks that don't reference their
  // own .tif. PS2 weapon atlases share one sheet across all chunks.
  defaultTexture?: string | null;
  // Fallback single texture (used if chunk has no specific texture).
  textureRgba?: ModelTexturePixels | null;
  // Multiply the baked per-vertex colours from the model's DMA chains into
  // the material (the game's lighting for maps, props and vehicles). On by
  // default; chunks without colours get a neutral 1.0.
  vertexColors?: boolean;
  // Map-viewer style controls (HTML reference). Defaults preserve current
  // weapon-preview behaviour, so untouched options leave the renderer alone.
  triStride?: number;        // 1 = full, N = every Nth triangle.
  topDown?: boolean;         // orthographic top-down camera.
  unlit?: boolean;           // ignore directional light (ambient only).
  solidColor?: boolean;      // skip texture sampling, render flat material.
  transparentTextures?: boolean;
  wireOverlay?: boolean;
  wireOverlayColor?: number;
  cameraView?: ModelCameraView;
  referenceBounds?: { label?: string; bounds: ModelBounds3 } | null;
  showBounds?: boolean;
}

export class ZedModelRenderer {
  private readonly renderer: THREE.WebGLRenderer;
  private readonly scene = new THREE.Scene();
  private readonly camera = new THREE.PerspectiveCamera(45, 4 / 3, 0.01, 5000);
  private readonly root = new THREE.Group();
  private readonly resizeObserver: ResizeObserver;

  private frameId = 0;
  private mesh: THREE.Mesh | null = null;
  private wireOverlay: THREE.LineSegments | null = null;
  private boundsGuide: THREE.Group | null = null;
  private materials: THREE.Material[] = [];
  private textures: THREE.DataTexture[] = [];
  private boundingRadius = 1;

  private yaw = 0.6;
  private pitch = -0.3;
  private distance = 4;
  private cameraZoom = 1;
  private wireframe = false;

  private readonly drag: DragState = { active: false, lastX: 0, lastY: 0 };

  public get attachedCanvas(): HTMLCanvasElement {
    return this.canvas;
  }

  constructor(private readonly canvas: HTMLCanvasElement) {
    this.renderer = new THREE.WebGLRenderer({ canvas, antialias: true, alpha: true });
    // Leave the framebuffer transparent so the canvas' own blueprint-style
    // background remains visible behind the model.
    this.renderer.setClearColor(0x111318, 0);
    this.renderer.setPixelRatio(Math.min(window.devicePixelRatio || 1, 2));
    // PS2 textures are stored in their final display form — no sRGB tag
    // needed. Tell Three.js to treat the framebuffer as linear so the
    // output sampler doesn't gamma-correct an already-correct image (this
    // was making the world model look washed out compared to the HTML
    // reference renderer).
    this.renderer.outputColorSpace = THREE.LinearSRGBColorSpace;

    this.scene.add(this.root);

    // Brighter ambient + dimmer key light to better approximate the HTML
    // map-viewer's flat-lit look (which uses fog + a single directional
    // term rather than a full PBR setup).
    const ambient = new THREE.AmbientLight(0xffffff, 1.05);
    const key = new THREE.DirectionalLight(0xffffff, 0.35);
    key.position.set(2, 2, 3);
    this.scene.add(ambient, key);

    this.resizeObserver = new ResizeObserver(() => this.resize());
    this.resizeObserver.observe(canvas);

    canvas.addEventListener('mousedown', this.onMouseDown);
    canvas.addEventListener('mousemove', this.onMouseMove);
    canvas.addEventListener('mouseup', this.onMouseUp);
    canvas.addEventListener('mouseleave', this.onMouseUp);
    canvas.addEventListener('wheel', this.onWheel, { passive: false });

    this.resize();
    this.animate();
  }

  setModel(model: ZedModel | null, options: ModelRendererOptions = {}): void {
    this.disposeMesh();
    if (!model || model.chunks.length === 0) { return; }

    // Restrict to a single chunk if requested.
    const visibleChunks =
      options.chunkIndex !== undefined &&
      options.chunkIndex >= 0 &&
      options.chunkIndex < model.chunks.length
        ? [model.chunks[options.chunkIndex]]
        : model.chunks;

    this.wireframe = !!options.wireframe;
    const textures = options.textures ?? {};
    // `solidColor` map-viewer mode skips texture sampling entirely so the
    // chunks render as flat material — useful for inspecting topology.
    const solidColorMode = !!options.solidColor;
    const transparentTextures = !!options.transparentTextures;
    // `unlit` removes the directional light contribution; everything is lit
    // by the ambient only. We approximate by using MeshBasicMaterial instead
    // of MeshStandardMaterial for textured chunks.
    const unlit = !!options.unlit;
    // Subsamples the triangle list: 1 = full, 2 = every other, etc. Mirrors
    // the HTML map viewer's triStep dropdown.
    const triStride = Math.max(1, options.triStride ?? 1);
    // Baked vertex colours are the game's lighting: when the model carries
    // them, render it pre-lit (no scene light) and multiply them in.
    const useVertexColors = (options.vertexColors ?? true) && !solidColorMode &&
      visibleChunks.some(c => c.hasVertexColors);
    const prelit = unlit || useVertexColors;

    // Build the per-material texture cache. Each unique .tif becomes one
    // DataTexture + Mesh material. Chunks with no matching .tif fall
    // back to a plain grey material.
    const makeMaterial = (color: number, map?: THREE.DataTexture, pixels?: ModelTexturePixels): THREE.Material => {
      // Per-texture alpha. Effects keep the soft-blend setup they had
      // (transparentTextures). Otherwise the GS alpha test from the
      // texture's TEST register wins, and textures with transparent texels
      // but no test get a cutout threshold so fences, foliage and decals
      // read correctly.
      const gsAlphaTest = pixels?.alphaTest ?? null;
      let alphaTest: number;
      let translucent: boolean;
      if (transparentTextures) {
        alphaTest = gsAlphaTest ?? 0.02;
        translucent = true;
      } else if (gsAlphaTest !== null) {
        alphaTest = gsAlphaTest;
        translucent = !!pixels?.hasTransparency && gsAlphaTest < 0.5;
      } else if (pixels?.hasTransparency) {
        alphaTest = 0.5;
        translucent = false;
      } else {
        alphaTest = 0;
        translucent = false;
      }
      const base = {
        color,
        side: THREE.DoubleSide,
        wireframe: this.wireframe,
        transparent: translucent,
        depthWrite: !translucent,
        alphaTest,
        vertexColors: useVertexColors,
      };
      const params = map ? { ...base, map } : base;
      if (prelit) {
        return new THREE.MeshBasicMaterial(params);
      }
      return new THREE.MeshStandardMaterial({
        ...params,
        metalness: 0,
        roughness: 0.85,
      });
    };
    const materialIndexByTif = new Map<string, number>();
    const fallbackMaterial = makeMaterial(0xb8b8b8);
    this.materials = [fallbackMaterial];
    const fallbackTexture = options.textureRgba ?? null;

    const materialFor = (tifName: string | null): number => {
      // solidColor mode short-circuits to the flat fallback material so
      // there's no per-chunk texture lookup at all.
      if (solidColorMode) { return 0; }
      // Fall back to the model-wide atlas texture if this chunk doesn't
      // carry its own .tif reference.
      const effective = tifName ?? options.defaultTexture ?? null;
      if (!effective) { return 0; }
      const key = effective.toLowerCase();
      const existing = materialIndexByTif.get(key);
      if (existing !== undefined) { return existing; }
      // Cap the number of unique materials per model. Past this limit the
      // chunk just gets the fallback grey material — better than crashing
      // the renderer process by allocating dozens of WebGL textures.
      if (this.materials.length >= MAX_UNIQUE_MATERIALS) { return 0; }
      const tex = textures[key] ?? fallbackTexture;
      if (!tex) { return 0; }
      // Sanity-check the decoded texture so a malformed header doesn't
      // bring down the GPU process. PS2 textures are always 2^N square in
      // practice; anything bigger than 2048 or smaller than 4 is junk.
      if (!Number.isFinite(tex.width) || !Number.isFinite(tex.height)) { return 0; }
      if (tex.width < 4 || tex.height < 4) { return 0; }
      if (tex.width > MAX_TEXTURE_DIMENSION || tex.height > MAX_TEXTURE_DIMENSION) { return 0; }
      const expectedLen = tex.width * tex.height * 4;
      if (tex.rgba.length < expectedLen) { return 0; }
      const dt = new THREE.DataTexture(
        tex.rgba,
        tex.width,
        tex.height,
        THREE.RGBAFormat,
        THREE.UnsignedByteType,
      );
      dt.flipY = false;
      // PS2 textures are stored in display-ready colour. Tagging them as
      // sRGB makes Three.js gamma-decode them on sample, which double-
      // corrects and washes out the colours vs. the HTML reference.
      dt.colorSpace = THREE.LinearSRGBColorSpace;
      // Closer to PS2 bilinear: linear mag, no mipmap (PS2 rarely uses
      // them in fixed-camera shots), repeat wrap for atlas tiling.
      dt.magFilter = THREE.LinearFilter;
      dt.minFilter = THREE.LinearFilter;
      dt.generateMipmaps = false;
      // Wrap modes come from the texture's GS CLAMP register when known.
      dt.wrapS = tex.wrapS === 'clamp' ? THREE.ClampToEdgeWrapping : THREE.RepeatWrapping;
      dt.wrapT = tex.wrapT === 'clamp' ? THREE.ClampToEdgeWrapping : THREE.RepeatWrapping;
      dt.needsUpdate = true;
      this.textures.push(dt);
      const mat = makeMaterial(0xffffff, dt, tex);
      this.materials.push(mat);
      const idx = this.materials.length - 1;
      materialIndexByTif.set(key, idx);
      return idx;
    };

    // Pack each visible chunk's vertices + indices into one BufferGeometry,
    // adding a draw group per chunk so its material is applied.
    const allPositions: number[] = [];
    const allUvs: number[] = [];
    const allColors: number[] = [];
    const allIndices: number[] = [];
    const groups: Array<{ start: number; count: number; material: number }> = [];
    let vBase = 0;
    let iBase = 0;
    const uvMode = options.uvMode ?? 'direct';
    const transformUv = (u: number, v: number): [number, number] => {
      let outU = u;
      let outV = v;
      if (uvMode === 'swap' || uvMode === 'swapFlipV') {
        const t = outU; outU = outV; outV = t;
      }
      if (uvMode === 'flipV' || uvMode === 'swapFlipV') {
        outV = 1 - outV;
      }
      return [outU, outV];
    };
    let bailedOnSize = false;
    for (const chunk of visibleChunks) {
      // Hard stop: a single malformed chunk shouldn't be able to allocate
      // millions of floats and tip the renderer process over the OOM line.
      // PS2 chunks are at most ~4k verts in practice; anything past
      // MAX_VERTICES_TOTAL is almost certainly a mis-parsed payload (likely
      // a UI / scripting blob that happened to satisfy the chunk magic).
      if (vBase + chunk.vertices.length > MAX_VERTICES_TOTAL) { bailedOnSize = true; break; }
      if (iBase + chunk.indices.length > MAX_INDICES_TOTAL) { bailedOnSize = true; break; }
      const chunkHasColors = !!chunk.hasVertexColors;
      for (const v of chunk.vertices) {
        allPositions.push(v.x, v.y, v.z);
        const [tu, tv] = transformUv(v.u, v.v);
        allUvs.push(tu, tv);
        if (useVertexColors) {
          // GS modulation: texel * colour / 128, so 0x80 is neutral and
          // 0xFF doubles the brightness. Chunks without a colour block
          // draw at 1.0.
          if (chunkHasColors) {
            allColors.push((v.r ?? 128) / 128, (v.g ?? 128) / 128, (v.b ?? 128) / 128);
          } else {
            allColors.push(1, 1, 1);
          }
        }
      }
      // Apply the triangle stride here so the stride applies per-chunk.
      // Each chunk.indices is laid out as flat triples (a, b, c, a, b, c…).
      let kept = 0;
      for (let t = 0; t < chunk.indices.length; t += 3) {
        const triIndex = t / 3;
        if (triStride > 1 && triIndex % triStride !== 0) { continue; }
        allIndices.push(
          vBase + chunk.indices[t + 0],
          vBase + chunk.indices[t + 1],
          vBase + chunk.indices[t + 2],
        );
        kept += 3;
      }
      groups.push({ start: iBase, count: kept, material: materialFor(chunk.textureName) });
      vBase += chunk.vertices.length;
      iBase += kept;
    }

    if (bailedOnSize) {
      console.warn(
        `[ZedModelRenderer] model exceeded vertex/index caps; skipping render. ` +
          `vBase=${vBase} iBase=${iBase}`,
      );
    }
    if (allIndices.length === 0) { return; }

    const geometry = new THREE.BufferGeometry();
    geometry.setAttribute('position', new THREE.BufferAttribute(new Float32Array(allPositions), 3));
    geometry.setAttribute('uv', new THREE.BufferAttribute(new Float32Array(allUvs), 2));
    if (useVertexColors) {
      geometry.setAttribute('color', new THREE.BufferAttribute(new Float32Array(allColors), 3));
    }
    geometry.setIndex(allIndices);
    geometry.computeVertexNormals();

    // Re-center the geometry so it sits in front of the camera regardless of
    // where the model authored its world position.
    geometry.computeBoundingBox();
    const box = geometry.boundingBox;
    const originalCenter = new THREE.Vector3();
    if (box) {
      const cx = (box.min.x + box.max.x) / 2;
      const cy = (box.min.y + box.max.y) / 2;
      const cz = (box.min.z + box.max.z) / 2;
      originalCenter.set(cx, cy, cz);
      geometry.translate(-cx, -cy, -cz);
    }
    geometry.computeBoundingSphere();
    geometry.computeBoundingBox();

    for (const g of groups) {
      geometry.addGroup(g.start, g.count, g.material);
    }

    this.boundingRadius = geometry.boundingSphere?.radius || 1;
    const referenceBox = options.referenceBounds
      ? boundsToBox3(options.referenceBounds.bounds).translate(originalCenter.clone().multiplyScalar(-1))
      : null;
    const modelBox = geometry.boundingBox?.clone() ?? null;
    const fitRadius = cameraFitRadius(this.boundingRadius, modelBox, referenceBox, options.cameraView);
    this.distance = fitRadius * 2.75 || 4;
    this.cameraZoom = 1;
    this.updateCameraProjection();

    this.mesh = new THREE.Mesh(geometry, this.materials);
    // These payloads often have unusual authoring bounds. A false-positive
    // frustum cull looks like random model parts disappearing while orbiting,
    // so keep debug/model payloads explicitly visible.
    this.mesh.frustumCulled = false;
    if (options.flipY) {
      this.mesh.rotation.x = Math.PI;
    }
    this.root.add(this.mesh);

    if (options.wireOverlay && !this.wireframe) {
      const wireGeometry = new THREE.WireframeGeometry(geometry);
      const wireMaterial = new THREE.LineBasicMaterial({
        color: options.wireOverlayColor ?? 0xffd36a,
        transparent: true,
        opacity: 0.9,
        depthWrite: false,
      });
      this.wireOverlay = new THREE.LineSegments(wireGeometry, wireMaterial);
      this.wireOverlay.frustumCulled = false;
      this.root.add(this.wireOverlay);
    }

    if (options.showBounds || referenceBox) {
      this.boundsGuide = new THREE.Group();
      this.boundsGuide.renderOrder = 20;
      if (modelBox) {
        this.boundsGuide.add(createBoxGuide(modelBox, 0x8ea8b8, 0.45));
      }
      if (referenceBox) {
        this.boundsGuide.add(createBoxGuide(referenceBox, 0x4ec9b0, 0.95));
      }
      this.root.add(this.boundsGuide);
    }

    this.applyCameraView(options.cameraView ?? (options.topDown ? 'top' : 'auto'), !!referenceBox);
  }

  setFlipY(value: boolean): void {
    if (this.mesh) {
      this.mesh.rotation.x = value ? Math.PI : 0;
    }
  }

  setWireframe(value: boolean): void {
    this.wireframe = value;
    for (const mat of this.materials) {
      if ('wireframe' in mat) {
        (mat as THREE.MeshStandardMaterial).wireframe = value;
      }
    }
  }

  dispose(): void {
    cancelAnimationFrame(this.frameId);
    this.resizeObserver.disconnect();
    this.canvas.removeEventListener('mousedown', this.onMouseDown);
    this.canvas.removeEventListener('mousemove', this.onMouseMove);
    this.canvas.removeEventListener('mouseup', this.onMouseUp);
    this.canvas.removeEventListener('mouseleave', this.onMouseUp);
    this.canvas.removeEventListener('wheel', this.onWheel);
    this.disposeMesh();
    this.renderer.dispose();
  }

  private animate = (): void => {
    this.frameId = requestAnimationFrame(this.animate);
    this.resize();
    const r = this.distance;
    this.camera.zoom = this.cameraZoom;
    this.camera.position.set(
      r * Math.cos(this.pitch) * Math.sin(this.yaw),
      r * Math.sin(this.pitch),
      r * Math.cos(this.pitch) * Math.cos(this.yaw),
    );
    this.camera.lookAt(0, 0, 0);
    this.renderer.render(this.scene, this.camera);
  };

  private resize(): void {
    const width = Math.max(1, this.canvas.clientWidth);
    const height = Math.max(1, this.canvas.clientHeight);
    const drawing = this.renderer.getDrawingBufferSize(new THREE.Vector2());
    const pixelRatio = this.renderer.getPixelRatio();
    if (drawing.x !== Math.floor(width * pixelRatio) || drawing.y !== Math.floor(height * pixelRatio)) {
      this.renderer.setSize(width, height, false);
      this.camera.aspect = width / height;
      this.updateCameraProjection();
    }
  }

  private onMouseDown = (e: MouseEvent): void => {
    this.drag.active = true;
    this.drag.lastX = e.clientX;
    this.drag.lastY = e.clientY;
  };

  private onMouseMove = (e: MouseEvent): void => {
    if (!this.drag.active) { return; }
    const dx = e.clientX - this.drag.lastX;
    const dy = e.clientY - this.drag.lastY;
    this.drag.lastX = e.clientX;
    this.drag.lastY = e.clientY;
    this.yaw -= dx * 0.01;
    this.pitch = Math.max(-Math.PI / 2 + 0.05, Math.min(Math.PI / 2 - 0.05, this.pitch + dy * 0.01));
  };

  private onMouseUp = (): void => {
    this.drag.active = false;
  };

  private onWheel = (e: WheelEvent): void => {
    e.preventDefault();
    const factor = e.deltaY > 0 ? 1 / 1.1 : 1.1;
    // Match the standalone HTML viewer's feel: zoom the view without moving
    // the camera through the object, which avoids near-plane clipping while
    // orbiting around detailed models.
    this.cameraZoom = Math.max(0.08, Math.min(40, this.cameraZoom * factor));
    this.updateCameraProjection();
  };

  private updateCameraProjection(): void {
    const radius = Math.max(1e-3, this.boundingRadius);
    this.camera.near = Math.max(0.01, radius / 1000);
    this.camera.far = Math.max(100, this.distance + radius * 8);
    this.camera.zoom = this.cameraZoom;
    this.camera.updateProjectionMatrix();
  }

  private disposeMesh(): void {
    if (this.mesh) {
      this.root.remove(this.mesh);
      this.mesh.geometry.dispose();
      this.mesh = null;
    }
    if (this.wireOverlay) {
      this.root.remove(this.wireOverlay);
      this.wireOverlay.geometry.dispose();
      const material = this.wireOverlay.material;
      if (Array.isArray(material)) {
        for (const m of material) { m.dispose(); }
      } else {
        material.dispose();
      }
      this.wireOverlay = null;
    }
    for (const m of this.materials) { m.dispose(); }
    this.materials = [];
    for (const t of this.textures) { t.dispose(); }
    this.textures = [];
    if (this.boundsGuide) {
      this.root.remove(this.boundsGuide);
      this.boundsGuide.traverse(obj => {
        if (obj instanceof THREE.LineSegments) {
          obj.geometry.dispose();
          const material = obj.material;
          if (Array.isArray(material)) {
            for (const m of material) { m.dispose(); }
          } else {
            material.dispose();
          }
        }
      });
      this.boundsGuide = null;
    }
  }

  private applyCameraView(view: ModelCameraView, hasReferenceBounds: boolean): void {
    const effective = view === 'auto'
      ? 'iso'
      : view === 'mapTop' && !hasReferenceBounds
        ? 'top'
        : view === 'mapIso' && !hasReferenceBounds
          ? 'iso'
          : view;
    switch (effective) {
      case 'top':
      case 'mapTop':
        this.pitch = -Math.PI / 2 + 0.001;
        this.yaw = 0;
        break;
      case 'front':
        this.pitch = 0;
        this.yaw = 0;
        break;
      case 'back':
        this.pitch = 0;
        this.yaw = Math.PI;
        break;
      case 'left':
        this.pitch = 0;
        this.yaw = -Math.PI / 2;
        break;
      case 'right':
        this.pitch = 0;
        this.yaw = Math.PI / 2;
        break;
      case 'mapIso':
      case 'iso':
      default:
        this.pitch = -0.3;
        this.yaw = 0.6;
        break;
    }
  }
}

function boundsToBox3(bounds: ModelBounds3): THREE.Box3 {
  return new THREE.Box3(
    new THREE.Vector3(bounds.minX, bounds.minY, bounds.minZ),
    new THREE.Vector3(bounds.maxX, bounds.maxY, bounds.maxZ),
  );
}

function radiusFromBox(box: THREE.Box3 | null): number {
  if (!box) { return 0; }
  const size = new THREE.Vector3();
  box.getSize(size);
  return Math.max(0.001, size.length() * 0.5);
}

function cameraFitRadius(modelRadius: number, modelBox: THREE.Box3 | null, referenceBox: THREE.Box3 | null, view?: ModelCameraView): number {
  if (!referenceBox || (view !== 'mapTop' && view !== 'mapIso')) {
    return Math.max(0.001, modelRadius);
  }
  const combined = new THREE.Box3();
  if (modelBox) { combined.union(modelBox); }
  combined.union(referenceBox);
  return Math.max(modelRadius, radiusFromBox(combined));
}

function createBoxGuide(box: THREE.Box3, color: number, opacity: number): THREE.LineSegments {
  const min = box.min;
  const max = box.max;
  const pts = [
    min.x, min.y, min.z, max.x, min.y, min.z,
    max.x, min.y, min.z, max.x, min.y, max.z,
    max.x, min.y, max.z, min.x, min.y, max.z,
    min.x, min.y, max.z, min.x, min.y, min.z,

    min.x, max.y, min.z, max.x, max.y, min.z,
    max.x, max.y, min.z, max.x, max.y, max.z,
    max.x, max.y, max.z, min.x, max.y, max.z,
    min.x, max.y, max.z, min.x, max.y, min.z,

    min.x, min.y, min.z, min.x, max.y, min.z,
    max.x, min.y, min.z, max.x, max.y, min.z,
    max.x, min.y, max.z, max.x, max.y, max.z,
    min.x, min.y, max.z, min.x, max.y, max.z,
  ];
  const geometry = new THREE.BufferGeometry();
  geometry.setAttribute('position', new THREE.Float32BufferAttribute(pts, 3));
  const material = new THREE.LineBasicMaterial({
    color,
    transparent: true,
    opacity,
    depthTest: false,
    depthWrite: false,
  });
  const lines = new THREE.LineSegments(geometry, material);
  lines.frustumCulled = false;
  lines.renderOrder = 20;
  return lines;
}
