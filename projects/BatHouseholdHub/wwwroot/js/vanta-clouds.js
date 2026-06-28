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
            speed: 0.7,
            skyColor: 0x0c1118,
            cloudColor: 0x2c3744,
            cloudShadowColor: 0x05070a,
            sunColor: 0xffb877,
            sunGlareColor: 0xff9f43,
            sunlightColor: 0xffd4a3
        });
    },
    dispose() {
        if (this.instance) {
            this.instance.destroy();
            this.instance = null;
        }
    }
};
