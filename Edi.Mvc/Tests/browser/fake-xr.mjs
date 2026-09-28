// A hardware-free WebXR boundary. Three.js, WebGL, video textures, DOM panels
// and controller polling run unchanged; only headset poses/session are fake.
export async function installFakeXr(page) {
    await page.evaluate(async () => {
        const THREE = await import('/lib/vr/three.module.min.js');
        const panels = [], add = THREE.Group.prototype.add;
        THREE.Group.prototype.add = function(...objects) {
            panels.push(...objects.filter(mesh => mesh.material?.map?.isCanvasTexture));
            return add.apply(this, objects);
        };
        const transform = (position, rotation = [0, 0, 0, 1]) => {
            const p = new THREE.Vector3(...position), q = new THREE.Quaternion(...rotation);
            return { position: p, orientation: q, matrix: new THREE.Matrix4().compose(p, q, new THREE.Vector3(1, 1, 1)).elements };
        };
        const projectionMatrix = new THREE.PerspectiveCamera(90, 1, .03, 100).projectionMatrix.elements;
        const source = { handedness: 'right', profiles: ['oculus-touch-v3'], targetRayMode: 'tracked-pointer',
            targetRaySpace: {}, gripSpace: {},
            gamepad: { mapping: 'xr-standard', axes: [0, 0, 0, 0], buttons: Array.from({ length: 6 }, () => ({ pressed: false, touched: false, value: 0 })) } };
        class Session extends EventTarget {
            renderState = { depthNear: .03, depthFar: 100, baseLayer: null };
            enabledFeatures = ['viewer', 'local']; visibilityState = 'visible'; environmentBlendMode = 'opaque';
            inputSources = []; headPosition = [0, 1.6, 0]; headRotation = [0, 0, 0, 1];
            rayPosition = [.2, 1.3, 0]; rayRotation = [0, 0, 0, 1]; frames = 0; ended = false; clock = null;
            updateRenderState(state) { Object.assign(this.renderState, state); }
            async requestReferenceSpace() {
                setTimeout(() => {
                    this.inputSources = [source];
                    const event = new Event('inputsourceschange');
                    Object.assign(event, { added: [source], removed: [] }); this.dispatchEvent(event);
                }, 0);
                return new EventTarget();
            }
            frame() {
                const session = this;
                return { session,
                    getViewerPose() {
                        return { transform: transform(session.headPosition, session.headRotation), views: ['left', 'right'].map((eye, index) => ({
                            eye, projectionMatrix, transform: transform([session.headPosition[0] + (index ? .032 : -.032), session.headPosition[1], session.headPosition[2]], session.headRotation)
                        })) };
                    },
                    getPose() { return { transform: transform(session.rayPosition, session.rayRotation), emulatedPosition: false }; }
                };
            }
            requestAnimationFrame(callback) {
                return window.requestAnimationFrame(time => { if (!this.ended) { this.frames++; callback(this.clock ?? time, this.frame()); } });
            }
            cancelAnimationFrame(id) { window.cancelAnimationFrame(id); }
            trigger(type) {
                const event = new Event(type); Object.assign(event, { inputSource: source, frame: this.frame() }); this.dispatchEvent(event);
            }
            pointAt(x, y, z) {
                const direction = new THREE.Vector3(x, y, z).sub(new THREE.Vector3(...this.rayPosition)).normalize();
                this.rayRotation = new THREE.Quaternion().setFromUnitVectors(new THREE.Vector3(0, 0, -1), direction).toArray();
            }
            async end() { if (!this.ended) { this.ended = true; this.dispatchEvent(new Event('end')); } }
        }
        class Layer {
            framebuffer = null; framebufferWidth = 1024; framebufferHeight = 512; ignoreDepthValues = false;
            getViewport(view) { return { x: view.eye === 'left' ? 0 : 512, y: 0, width: 512, height: 512 }; }
        }
        Object.defineProperty(window, 'XRWebGLBinding', { configurable: true, value: undefined });
        Object.defineProperty(window, 'XRWebGLLayer', { configurable: true, value: Layer });
        WebGL2RenderingContext.prototype.makeXRCompatible = async () => {};
        window.fakeXr = { source, session: null, panels };
        Object.defineProperty(navigator, 'xr', { configurable: true, value: {
            async isSessionSupported() { return true; },
            async requestSession() { return window.fakeXr.session = new Session(); }
        } });
        // Let the page's real capability probe settle before overriding its
        // result, otherwise a late `isSessionSupported` resolution can hide the
        // controls again after the fake XR implementation is installed.
        await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)));
        document.querySelector('#enterVr').hidden = false;
        document.querySelector('#vrFollowToggle').hidden = false;
    });
}
