function urlBase64ToUint8Array(base64String) {
    const padding = '='.repeat((4 - base64String.length % 4) % 4);
    const base64 = (base64String + padding).replace(/-/g, '+').replace(/_/g, '/');
    const raw = atob(base64);
    return Uint8Array.from([...raw].map(c => c.charCodeAt(0)));
}

window.householdPush = {
    isSupported: () => 'serviceWorker' in navigator && 'PushManager' in window,

    getStatus: async () => {
        if (!window.householdPush.isSupported()) return 'unsupported';
        const reg = await navigator.serviceWorker.ready;
        const sub = await reg.pushManager.getSubscription();
        return sub ? 'subscribed' : 'unsubscribed';
    },

    subscribe: async (owner) => {
        const perm = await Notification.requestPermission();
        if (perm !== 'granted') return 'denied';

        const keyResponse = await fetch('/api/push/public-key');
        const publicKey = (await keyResponse.text()).trim();
        if (!publicKey) return 'unconfigured';

        const reg = await navigator.serviceWorker.ready;
        const sub = await reg.pushManager.subscribe({
            userVisibleOnly: true,
            applicationServerKey: urlBase64ToUint8Array(publicKey)
        });

        const json = sub.toJSON();
        await fetch('/api/push/subscribe', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({
                endpoint: json.endpoint,
                p256dh: json.keys.p256dh,
                auth: json.keys.auth,
                owner
            })
        });
        return 'subscribed';
    },

    unsubscribe: async () => {
        const reg = await navigator.serviceWorker.ready;
        const sub = await reg.pushManager.getSubscription();
        if (!sub) return 'unsubscribed';
        await fetch('/api/push/unsubscribe', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ endpoint: sub.endpoint })
        });
        await sub.unsubscribe();
        return 'unsubscribed';
    }
};
