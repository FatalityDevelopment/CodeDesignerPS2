import * as THREE from 'three';
import { Ps2Icon, Ps2IconSys } from './ps2-icon-parser';

export class Ps2IconRenderer {
  private readonly renderer: THREE.WebGLRenderer;
  private readonly bgScene = new THREE.Scene();
  private readonly scene = new THREE.Scene();
  private readonly bgCamera = new THREE.OrthographicCamera(-1, 1, 1, -1, 0, 1);
  private readonly camera = new THREE.PerspectiveCamera(50, 4 / 3, 0.1, 100);
  private readonly resizeObserver: ResizeObserver;

  private frameId = 0;
  private startMs = performance.now();
  private currentShape = -1;
  private icon: Ps2Icon | null = null;
  private bgObjects: THREE.Object3D[] = [];
  private geometries: THREE.BufferGeometry[] = [];
  private texture: THREE.DataTexture | null = null;
  private material: THREE.ShaderMaterial | null = null;
  private mesh: THREE.Mesh | null = null;

  constructor(private readonly canvas: HTMLCanvasElement) {
    this.renderer = new THREE.WebGLRenderer({
      canvas,
      alpha: true,
      antialias: true,
    });
    this.renderer.setClearColor(0x000000, 0);
    this.renderer.autoClear = false;
    this.renderer.setPixelRatio(Math.min(window.devicePixelRatio || 1, 2));

    this.camera.position.set(0, -4, -10);
    this.camera.up.set(0, -1, 0);
    this.camera.lookAt(0, -2, 0);

    this.resizeObserver = new ResizeObserver(() => this.resize());
    this.resizeObserver.observe(canvas);
    this.resize();
    this.animate();
  }

  setModel(iconSys: Ps2IconSys | null, icon: Ps2Icon | null): void {
    this.disposeSceneObjects();
    this.icon = icon;
    this.startMs = performance.now();
    this.currentShape = -1;

    if (iconSys) {
      this.buildBackground(iconSys);
    }

    if (icon && iconSys) {
      this.buildIcon(iconSys, icon);
    }
  }

  dispose(): void {
    cancelAnimationFrame(this.frameId);
    this.resizeObserver.disconnect();
    this.disposeSceneObjects();
    this.renderer.dispose();
  }

  private animate = (): void => {
    this.frameId = requestAnimationFrame(this.animate);
    this.resize();
    this.updateIconAnimation();
    this.renderer.clear();
    this.renderer.render(this.bgScene, this.bgCamera);
    this.renderer.clearDepth();
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

  private buildBackground(iconSys: Ps2IconSys): void {
    const skyGeo = new THREE.BufferGeometry();
    skyGeo.setAttribute('position', new THREE.Float32BufferAttribute([
      -1,  1, 0,
      -1, -1, 0,
       1,  1, 0,
       1, -1, 0,
       1,  1, 0,
      -1, -1, 0,
    ], 3));
    const sky = new THREE.Mesh(
      skyGeo,
      new THREE.MeshBasicMaterial({
        color: new THREE.Color(0.34, 0.34, 0.34),
        depthTest: false,
        depthWrite: false,
      }),
    );
    this.bgScene.add(sky);
    this.bgObjects.push(sky);

    const bgGeo = new THREE.BufferGeometry();
    bgGeo.setAttribute('position', new THREE.Float32BufferAttribute([
      -1,  1, 0,
      -1, -1, 0,
       1,  1, 0,
       1, -1, 0,
       1,  1, 0,
      -1, -1, 0,
    ], 3));

    const colors = [
      iconSys.bgColors[0],
      iconSys.bgColors[2],
      iconSys.bgColors[1],
      iconSys.bgColors[3],
      iconSys.bgColors[1],
      iconSys.bgColors[2],
    ].flatMap(c => [
      clamp01((c?.[0] ?? 80) / 255),
      clamp01((c?.[1] ?? 80) / 255),
      clamp01((c?.[2] ?? 80) / 255),
    ]);
    bgGeo.setAttribute('color', new THREE.Float32BufferAttribute(colors, 3));

    const bg = new THREE.Mesh(
      bgGeo,
      new THREE.MeshBasicMaterial({
        vertexColors: true,
        transparent: true,
        opacity: clamp01(iconSys.backgroundTransparency / 128),
        depthTest: false,
        depthWrite: false,
      }),
    );
    this.bgScene.add(bg);
    this.bgObjects.push(bg);
  }

  private buildIcon(iconSys: Ps2IconSys, icon: Ps2Icon): void {
    this.geometries = [];
    for (let shape = 0; shape < icon.animationShapes; shape++) {
      const nextShape = shape + 1 >= icon.animationShapes ? 0 : shape + 1;
      this.geometries.push(this.buildGeometry(icon, shape, nextShape));
    }

    if (icon.textureRgba) {
      this.texture = new THREE.DataTexture(
        icon.textureRgba,
        128,
        128,
        THREE.RGBAFormat,
        THREE.UnsignedByteType,
      );
      this.texture.flipY = false;
      this.texture.magFilter = THREE.LinearFilter;
      this.texture.minFilter = THREE.LinearFilter;
      this.texture.wrapS = THREE.ClampToEdgeWrapping;
      this.texture.wrapT = THREE.ClampToEdgeWrapping;
      this.texture.needsUpdate = true;
    }

    this.material = new THREE.ShaderMaterial({
      uniforms: {
        tweenFactor: { value: 0 },
        texture0: { value: this.texture },
        hasTexture: { value: Boolean(this.texture) },
        vertexColorStrength: { value: hasUsefulVertexColors(icon) ? 1 : 0 },
        ambient: { value: vec4(iconSys.ambient, [0.35, 0.35, 0.35, 1]) },
        lightDirs: { value: iconSys.lightDirs.map(v => vec4(v, [0, 0, -1, 0])) },
        lightColors: { value: iconSys.lightColors.map(v => vec4(v, [0.55, 0.55, 0.55, 1])) },
      },
      vertexShader: `
        attribute vec3 nextPosition;
        attribute vec4 vertexColor;
        uniform float tweenFactor;
        varying vec2 vUv;
        varying vec3 vNormal;
        varying vec4 vColor;

        void main() {
          vUv = uv;
          vColor = vertexColor;
          vNormal = normalize(normalMatrix * normal);
          vec3 pos = mix(position, nextPosition, tweenFactor);
          gl_Position = projectionMatrix * modelViewMatrix * vec4(pos, 1.0);
        }
      `,
      fragmentShader: `
        precision highp float;
        #define MAX_NUM_TOTAL_LIGHTS 3

        uniform sampler2D texture0;
        uniform bool hasTexture;
        uniform float vertexColorStrength;
        varying vec2 vUv;
        varying vec3 vNormal;
        varying vec4 vColor;

        void main() {
          vec4 base;
          if (hasTexture) {
            base = texture2D(texture0, vUv);
          } else {
            base = vec4(mix(vec3(0.82), vColor.rgb, vertexColorStrength), 1.0);
          }

          if (base.a < 0.05) {
            discard;
          }

          gl_FragColor = vec4(clamp(base.rgb, 0.0, 1.0), base.a);
        }
      `,
      side: THREE.DoubleSide,
      depthTest: true,
      depthWrite: true,
      transparent: true,
    });

    this.mesh = new THREE.Mesh(this.geometries[0], this.material);
    this.scene.add(this.mesh);
  }

  private buildGeometry(icon: Ps2Icon, shape: number, nextShape: number): THREE.BufferGeometry {
    const position = new Float32Array(icon.vertexCount * 3);
    const nextPosition = new Float32Array(icon.vertexCount * 3);
    const normal = new Float32Array(icon.vertexCount * 3);
    const uv = new Float32Array(icon.vertexCount * 2);
    const vertexColor = new Float32Array(icon.vertexCount * 4);

    for (let i = 0; i < icon.vertexCount; i++) {
      position.set(icon.vertices[i][shape], i * 3);
      nextPosition.set(icon.vertices[i][nextShape], i * 3);
      normal.set(icon.normals[i], i * 3);
      uv.set(icon.uvs[i], i * 2);
      vertexColor.set(icon.colors[i], i * 4);
    }

    const geo = new THREE.BufferGeometry();
    geo.setAttribute('position', new THREE.BufferAttribute(position, 3));
    geo.setAttribute('nextPosition', new THREE.BufferAttribute(nextPosition, 3));
    geo.setAttribute('normal', new THREE.BufferAttribute(normal, 3));
    geo.setAttribute('uv', new THREE.BufferAttribute(uv, 2));
    geo.setAttribute('vertexColor', new THREE.BufferAttribute(vertexColor, 4));
    geo.computeBoundingSphere();
    geo.computeBoundingBox();
    return geo;
  }

  private updateIconAnimation(): void {
    if (!this.icon || !this.mesh || !this.material || this.geometries.length === 0) {
      return;
    }

    const elapsed = (performance.now() - this.startMs) / 1000;
    const frameLength = Math.max(1, this.icon.frameLength);
    const shapeCount = Math.max(1, this.icon.animationShapes);
    const frame = Math.floor(elapsed * 60 * this.icon.animSpeed) % frameLength;
    const framesPerShape = Math.max(1, frameLength / shapeCount);
    const shape = Math.min(shapeCount - 1, Math.floor(frame / framesPerShape));
    const tween = (frame % framesPerShape) / framesPerShape;

    if (shape !== this.currentShape) {
      this.currentShape = shape;
      this.mesh.geometry = this.geometries[shape];
    }

    this.material.uniforms['tweenFactor'].value = tween;
    this.mesh.rotation.y = elapsed / 2;
  }

  private disposeSceneObjects(): void {
    for (const obj of this.bgObjects) {
      this.bgScene.remove(obj);
      const mesh = obj as THREE.Mesh;
      mesh.geometry?.dispose();
      if (Array.isArray(mesh.material)) {
        mesh.material.forEach(m => m.dispose());
      } else {
        mesh.material?.dispose();
      }
    }
    this.bgObjects = [];

    if (this.mesh) {
      this.scene.remove(this.mesh);
      this.mesh = null;
    }
    for (const geo of this.geometries) {
      geo.dispose();
    }
    this.geometries = [];
    this.material?.dispose();
    this.material = null;
    this.texture?.dispose();
    this.texture = null;
  }
}

function vec4(value: number[] | undefined, fallback: number[]): THREE.Vector4 {
  const v = value && value.length >= 4 ? value : fallback;
  return new THREE.Vector4(v[0], v[1], v[2], v[3]);
}

function hasUsefulVertexColors(icon: Ps2Icon): boolean {
  if (icon.colors.length === 0) {
    return false;
  }

  let total = 0;
  for (const color of icon.colors) {
    total += (color[0] ?? 0) + (color[1] ?? 0) + (color[2] ?? 0);
  }

  return total / (icon.colors.length * 3) > 0.04;
}

function clamp01(value: number): number {
  if (!Number.isFinite(value)) {
    return 0;
  }
  return Math.max(0, Math.min(1, value));
}
