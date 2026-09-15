// SPEC-20260915-wasm-boot-proxy-fix RF-002/RF-003: corporate proxies may block
// downloads by extension (.dat/.wasm/...). Remap every non-.js _framework boot
// asset to the extensionless same-origin route /framework-assets/{file}. The
// endpoint serves identical bytes, so the boot integrity hash still validates;
// on any failure we fall back to the default URI (dev / non-proxy setups).
(function () {
    "use strict";

    var frameworkSegment = "/_framework/";

    function remappedFetch(defaultUri, integrity) {
        var path = new URL(defaultUri, document.baseURI).pathname;
        var fileName = path.substring(path.lastIndexOf("/") + 1);
        var mirror = new URL("framework-assets/" + fileName, document.baseURI);
        var init = integrity ? { integrity: integrity } : {};
        return fetch(mirror, init).then(
            function (response) {
                return response.ok ? response : fetch(defaultUri, init);
            },
            function () {
                return fetch(defaultUri, init);
            });
    }

    Blazor.start({
        loadBootResource: function (type, name, defaultUri, integrity) {
            try {
                var path = new URL(defaultUri, document.baseURI).pathname;
                if (path.indexOf(frameworkSegment) === -1 || path.endsWith(".js")) {
                    return null; // default loading — proxies allow .js
                }
                return remappedFetch(defaultUri, integrity);
            } catch {
                return null;
            }
        }
    });
})();
