window.vantaClouds = {
    instance: null,
    init(elementId) {
        if (this.instance) this.instance.destroy();
        if (!window.VANTA || !window.VANTA.CLOUDS) return;
        const el = document.getElementById(elementId);
        if (!el) return;
        this.instance = window.VANTA.CLOUDS({
            el,
            mouseControls: true,
            touchControls: true,
            gyroControls: false,
            minHeight: 200,
            minWidth: 200,
            speed: 0.6,
            skyColor: 0x0d1d1a,
            cloudColor: 0x27403a,
            cloudShadowColor: 0x040b09,
            sunColor: 0xd8efe0,
            sunGlareColor: 0x9fcfc0,
            sunlightColor: 0xcfe9db
        });
    },
    dispose() {
        if (this.instance) {
            this.instance.destroy();
            this.instance = null;
        }
    }
};
