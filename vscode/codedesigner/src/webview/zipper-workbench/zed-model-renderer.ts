import * as THREE from 'three';
import type { ZedModel } from './zed-model-parser';

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

export interface ModelTextureMap {
  // Map from lowercased .tif name -> decoded texture pixels.
  [tifName: string]: { width: number; height: number; rgba: Uint8Array };
}

export type UvMode = 'direct' | 'flipV' | 'swap' | 'swapFlipV';

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
  textureRgba?: { width: number; height: number; rgba: Uint8Array } | null;
}

export class ZedModelRenderer {
  private readonly renderer: THREE.WebGLRenderer;
  private readonly scene = new THREE.Scene();
  private readonly camera = new THREE.PerspectiveCamera(45, 4 / 3, 0.01, 5000);
  private readonly root = new THREE.Group();
  private readonly resizeObserver: ResizeObserver;

  private frameId = 0;
  private mesh: THREE.Mesh | null = null;
  private materials: THREE.Material[] = [];
  private textures: THREE.DataTexture[] = [];
  private boundingRadius = 1;

  private yaw = 0.6;
  private pitch = -0.3;
  private distance = 4;
  private wireframe = false;

  private readonly drag: DragState = { active: false, lastX: 0, lastY: 0 };

  public get attachedCanvas(): HTMLCanvasElement {
    return this.canvas;
  }

  constructor(private readonly canvas: HTMLCanvasElement) {
    this.renderer = new THREE.WebGLRenderer({ canvas, antialias: true, alpha: true });
    this.renderer.setClearColor(0x222222, 1);
    this.renderer.setPixelRatio(Math.min(window.devicePixelRatio || 1, 2));

    this.scene.add(this.root);

    const ambient = new THREE.AmbientLight(0xffffff, 0.7);
    const key = new THREE.DirectionalLight(0xffffff, 0.7);
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

    // Build the per-material texture cache. Each unique .tif becomes one
    // DataTexture + MeshStandardMaterial. Chunks with no matching .tif fall
    // back to a plain grey material.
    const materialIndexByTif = new Map<string, number>();
    const fallbackMaterial = new THREE.MeshStandardMaterial({
      color: 0xb8b8b8,
      metalness: 0,
      roughness: 0.85,
      side: THREE.DoubleSide,
      wireframe: this.wireframe,
    });
    this.materials = [fallbackMaterial];

    const materialFor = (tifName: string | null): number => {
      // Fall back to the model-wide atlas texture if this chunk doesn't
      // carry its own .tif reference.
      const effective = tifName ?? options.defaultTexture ?? null;
      if (!effective) { return 0; }
      const key = effective.toLowerCase();
      const existing = materialIndexByTif.get(key);
      if (existing !== undefined) { return existing; }
      const tex = textures[key];
      if (!tex) { return 0; }
      const dt = new THREE.DataTexture(
        tex.rgba,
        tex.width,
        tex.height,
        THREE.RGBAFormat,
        THREE.UnsignedByteType,
      );
      dt.flipY = false;
      dt.colorSpace = THREE.SRGBColorSpace;
      dt.magFilter = THREE.LinearFilter;
      dt.minFilter = THREE.LinearFilter;
      dt.wrapS = THREE.RepeatWrapping;
      dt.wrapT = THREE.RepeatWrapping;
      dt.needsUpdate = true;
      this.textures.push(dt);
      const mat = new THREE.MeshStandardMaterial({
        color: 0xffffff,
        map: dt,
        metalness: 0,
        roughness: 0.85,
        side: THREE.DoubleSide,
        wireframe: this.wireframe,
      });
      this.materials.push(mat);
      const idx = this.materials.length - 1;
      materialIndexByTif.set(key, idx);
      return idx;
    };

    // Pack each visible chunk's vertices + indices into one BufferGeometry,
    // adding a draw group per chunk so its material is applied.
    const allPositions: number[] = [];
    const allUvs: number[] = [];
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
    for (const chunk of visibleChunks) {
      for (const v of chunk.vertices) {
        allPositions.push(v.x, v.y, v.z);
        const [tu, tv] = transformUv(v.u, v.v);
        allUvs.push(tu, tv);
      }
      for (const idx of chunk.indices) {
        allIndices.push(vBase + idx);
      }
      groups.push({ start: iBase, count: chunk.indices.length, material: materialFor(chunk.textureName) });
      vBase += chunk.vertices.length;
      iBase += chunk.indices.length;
    }

    if (allIndices.length === 0) { return; }

    const geometry = new THREE.BufferGeometry();
    geometry.setAttribute('position', new THREE.BufferAttribute(new Float32Array(allPositions), 3));
    geometry.setAttribute('uv', new THREE.BufferAttribute(new Float32Array(allUvs), 2));
    geometry.setIndex(allIndices);
    geometry.computeVertexNormals();

    // Re-center the geometry so it sits in front of the camera regardless of
    // where the model authored its world position.
    geometry.computeBoundingBox();
    const box = geometry.boundingBox;
    if (box) {
      const cx = (box.min.x + box.max.x) / 2;
      const cy = (box.min.y + box.max.y) / 2;
      const cz = (box.min.z + box.max.z) / 2;
      geometry.translate(-cx, -cy, -cz);
    }
    geometry.computeBoundingSphere();

    for (const g of groups) {
      geometry.addGroup(g.start, g.count, g.material);
    }

    this.boundingRadius = geometry.boundingSphere?.radius || 1;
    this.distance = this.boundingRadius * 2.5 || 4;

    this.mesh = new THREE.Mesh(geometry, this.materials);
    if (options.flipY) {
      this.mesh.rotation.x = Math.PI;
    }
    this.root.add(this.mesh);
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
      this.camera.updateProjectionMatrix();
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
    const factor = e.deltaY > 0 ? 1.1 : 1 / 1.1;
    this.distance = Math.max(this.boundingRadius * 0.3, Math.min(this.boundingRadius * 30, this.distance * factor));
  };

  private disposeMesh(): void {
    if (this.mesh) {
      this.root.remove(this.mesh);
      this.mesh.geometry.dispose();
      this.mesh = null;
    }
    for (const m of this.materials) { m.dispose(); }
    this.materials = [];
    for (const t of this.textures) { t.dispose(); }
    this.textures = [];
  }
}
