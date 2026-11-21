/* MDR | کامپوننت مشترک پیوست: mdr.attach(boxElement, entityName, entityId)
   نیازمند mdr (لایه‌ی Layout) و window.mdrText.attach */
(function () {
    "use strict";
    if (!window.mdr) return;

    function size(n) {
        if (n < 1024) return n + " B";
        if (n < 1048576) return (n / 1024).toFixed(1) + " KB";
        return (n / 1048576).toFixed(1) + " MB";
    }

    window.mdr.attach = function (box, entityName, entityId) {
        var T = (window.mdrText && window.mdrText.attach) || {};
        var base = "/Mdr/Files/";
        var q = "?entityName=" + encodeURIComponent(entityName) + "&entityId=" + encodeURIComponent(entityId);
        box.innerHTML = "<h4>" + mdr.esc(T.title) + '</h4><ul class="mdr-files"></ul><div class="mdr-fileform"></div><div class="mdr-filemsg"></div>';
        var ul = box.querySelector(".mdr-files"), form = box.querySelector(".mdr-fileform"), msg = box.querySelector(".mdr-filemsg");

        function say(t, bad) { msg.textContent = t || ""; msg.className = "mdr-filemsg" + (bad ? " is-bad" : ""); }

        function load() {
            mdr.get(base + "List" + q).then(function (d) {
                if (!d.success) { say(d.message || T.error, true); return; }
                ul.innerHTML = d.items.length ? d.items.map(function (f) {
                    return "<li><a href=\"" + base + "Download?id=" + f.FileGuid + '">' + mdr.esc(f.Title || f.OriginalFileName) + "</a> <small>" +
                        mdr.esc(f.OriginalFileName) + " · " + size(f.FileSize) + " · " + mdr.esc(f.CreatedByName || "") + " · " + mdr.esc(f.PersianDate || "") + "</small>" +
                        (f.CanDelete ? ' <button type="button" class="mdr-btn" data-del="' + f.FileGuid + '">' + mdr.esc(T.remove) + "</button>" : "") + "</li>";
                }).join("") : "<li><small>" + mdr.esc(T.none) + "</small></li>";

                form.innerHTML = d.canUpload
                    ? '<input type="file" class="mdr-filein"> <button type="button" class="mdr-btn" data-up="1">' + mdr.esc(T.add) + "</button> <small>" +
                      mdr.esc(T.max) + " " + d.maxMb + " MB</small>"
                    : "";
            }).catch(function () { say(T.error, true); });
        }

        box.addEventListener("click", function (e) {
            var del = e.target.closest("button[data-del]");
            if (del) {
                if (!confirm(T.confirmDelete)) return;
                mdr.post(base + "Delete", { id: del.getAttribute("data-del") }).then(function (r) { if (!r.success) say(r.message || T.error, true); else say(""); load(); });
                return;
            }
            if (e.target.closest("button[data-up]")) {
                var inp = form.querySelector(".mdr-filein");
                if (!inp.files.length) { say(T.nofile, true); return; }
                var fd = new FormData();
                fd.append("__RequestVerificationToken", mdr.token());
                fd.append("entityName", entityName); fd.append("entityId", entityId);
                fd.append("title", inp.files[0].name); fd.append("file", inp.files[0]);
                say(T.uploading);
                fetch(base + "Upload", { method: "POST", credentials: "same-origin", headers: { "X-Requested-With": "XMLHttpRequest" }, body: fd })
                    .then(function (r) { return r.json(); })
                    .then(function (r) { if (!r.success) say(r.message || T.error, true); else say(""); load(); })
                    .catch(function () { say(T.error, true); });
            }
        });

        load();
    };
})();
