window.householdPlaid = {
    openLink: (linkToken) => new Promise((resolve) => {
        const handler = Plaid.create({
            token: linkToken,
            onSuccess: (publicToken, metadata) => {
                resolve({ publicToken, institutionName: metadata?.institution?.name ?? "Bank" });
            },
            onExit: () => resolve(null)
        });
        handler.open();
    })
};
