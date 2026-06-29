self.addEventListener('push', event => {
    let data = { title: 'Household Hub', body: 'You have a new alert.', url: '/' };
    if (event.data) {
        try { data = event.data.json(); } catch { data.body = event.data.text(); }
    }
    event.waitUntil(self.registration.showNotification(data.title, {
        body: data.body,
        icon: '/images/icon-192.png',
        badge: '/images/icon-192.png',
        data: { url: data.url || '/' }
    }));
});

self.addEventListener('notificationclick', event => {
    event.notification.close();
    const url = event.notification.data?.url || '/';
    event.waitUntil(
        self.clients.matchAll({ type: 'window' }).then(clients => {
            for (const client of clients) {
                if (client.url.includes(self.location.origin) && 'focus' in client) {
                    client.navigate(url);
                    return client.focus();
                }
            }
            return self.clients.openWindow(url);
        })
    );
});
