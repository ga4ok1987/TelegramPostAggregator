window.channelsMonitorMiniApp = {
    getContext: function () {
        const webApp = window.Telegram && window.Telegram.WebApp ? window.Telegram.WebApp : null;

        if (webApp) {
            webApp.ready();
            webApp.expand();
            if (webApp.setHeaderColor) {
                webApp.setHeaderColor("#09172b");
            }
            if (webApp.setBackgroundColor) {
                webApp.setBackgroundColor("#060b14");
            }
        }

        const user = webApp && webApp.initDataUnsafe ? webApp.initDataUnsafe.user : null;

        return {
            initData: webApp ? webApp.initData || "" : "",
            isTelegramWebApp: !!webApp
        };
    },

    showAlert: function (message) {
        const webApp = window.Telegram && window.Telegram.WebApp ? window.Telegram.WebApp : null;
        if (webApp && webApp.showAlert) {
            webApp.showAlert(message);
            return;
        }

        window.alert(message);
    },

    showConfirm: function (message) {
        const webApp = window.Telegram && window.Telegram.WebApp ? window.Telegram.WebApp : null;
        if (webApp && webApp.showConfirm) {
            return new Promise(resolve => {
                webApp.showConfirm(message, confirmed => resolve(!!confirmed));
            });
        }

        return Promise.resolve(window.confirm(message));
    }
};
