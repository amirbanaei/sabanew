/* MDR | هسته‌ی مشترک اسکریپت صفحه‌ها: window.mdr = { esc, token, get, post }
   توکن CSRF از input[name=__RequestVerificationToken] صفحه خوانده می‌شود. */
window.mdr = {
    esc: function (s) {
        return String(s == null ? "" : s).replace(/[&<>"']/g, function (c) {
            return { "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c];
        });
    },
    token: function () { return document.querySelector("input[name=__RequestVerificationToken]").value; },
    get: function (url) {
        return fetch(url, { credentials: "same-origin", headers: { "X-Requested-With": "XMLHttpRequest" } })
            .then(function (r) { return r.json(); });
    },
    post: function (url, data) {
        var b = new URLSearchParams();
        b.append("__RequestVerificationToken", mdr.token());
        Object.keys(data).forEach(function (k) { if (data[k] != null) b.append(k, data[k]); });
        return fetch(url, {
            method: "POST", credentials: "same-origin",
            headers: { "Content-Type": "application/x-www-form-urlencoded", "X-Requested-With": "XMLHttpRequest" },
            body: b.toString()
        }).then(function (r) { return r.json(); });
    }
};

/* MDR | تاریخ شمسی (هم‌قالب فیلدهای «از تاریخ/تا تاریخ» صفحه‌ی تاریخچه‌ی رویدادها: jalalidatepicker با data-jdp)
   - مقدار داخل فیلد شمسی است (ارقام لاتین مثل خود تقویم)؛ همه‌ی صفحه‌ها و سرور با تاریخ میلادی ISO (yyyy-MM-dd یا yyyy-MM-ddTHH:mm) کار می‌کنند.
   - نمونه‌ی HTML فیلد:  mdr.jd.html("from", "2026-10-11")   و برای ساعت: mdr.jd.timeHtml("due-t", "2026-10-11T09:30")
   - خواندن/نوشتن:      mdr.jd.get(id) ، mdr.jd.set(id, iso) ، mdr.jd.getDT(dateId, timeId, defaultTime) ، mdr.jd.setDT(dateId, timeId, iso)
   تبدیل شمسی↔میلادی خودکفاست (الگوریتم ۳۳ساله‌ی jalaali) و به PersianCalendar/کتابخانه‌ی دیگری وابسته نیست. */
(function () {
    function div(a, b) { return ~~(a / b); }
    function mod(a, b) { return a - ~~(a / b) * b; }
    var BREAKS = [-61, 9, 38, 199, 426, 686, 756, 818, 1111, 1181, 1210, 1635, 2060, 2097, 2192, 2262, 2324, 2394, 2456, 3178];
    function jalCal(jy) {
        var bl = BREAKS.length, gy = jy + 621, leapJ = -14, jp = BREAKS[0], jm, jump, leap, leapG, march, n, i;
        for (i = 1; i < bl; i += 1) { jm = BREAKS[i]; jump = jm - jp; if (jy < jm) break; leapJ = leapJ + div(jump, 33) * 8 + div(mod(jump, 33), 4); jp = jm; }
        n = jy - jp; leapJ = leapJ + div(n, 33) * 8 + div(mod(n, 33) + 3, 4);
        if (mod(jump, 33) === 4 && jump - n === 4) leapJ += 1;
        leapG = div(gy, 4) - div((div(gy, 100) + 1) * 3, 4) - 150; march = 20 + leapJ - leapG;
        if (jump - n < 6) n = n - jump + div(jump + 4, 33) * 33;
        leap = mod(mod(n + 1, 33) - 1, 4); if (leap === -1) leap = 4;
        return { leap: leap, gy: gy, march: march };
    }
    function g2d(gy, gm, gd) { var d = div((gy + div(gm - 8, 6) + 100100) * 1461, 4) + div(153 * mod(gm + 9, 12) + 2, 5) + gd - 34840408; return d - div(div(gy + 100100 + div(gm - 8, 6), 100) * 3, 4) + 752; }
    function d2g(jdn) {
        var j = 4 * jdn + 139361631; j = j + div(div(4 * jdn + 183187720, 146097) * 3, 4) * 4 - 3908;
        var i = div(mod(j, 1461), 4) * 5 + 308, gd = div(mod(i, 153), 5) + 1, gm = mod(div(i, 153), 12) + 1, gy = div(j, 1461) - 100100 + div(8 - gm, 6);
        return { gy: gy, gm: gm, gd: gd };
    }
    function j2d(jy, jm, jd) { var r = jalCal(jy); return g2d(r.gy, 3, r.march) + (jm - 1) * 31 - div(jm, 7) * (jm - 7) + jd - 1; }
    function d2j(jdn) {
        var gy = d2g(jdn).gy, jy = gy - 621, r = jalCal(jy), jdn1f = g2d(gy, 3, r.march), jd, jm, k = jdn - jdn1f;
        if (k >= 0) { if (k <= 185) { jm = 1 + div(k, 31); jd = mod(k, 31) + 1; return { jy: jy, jm: jm, jd: jd }; } k -= 186; }
        else { jy -= 1; k += 179; if (r.leap === 1) k += 1; }
        jm = 7 + div(k, 30); jd = mod(k, 30) + 1; return { jy: jy, jm: jm, jd: jd };
    }
    function pad(n, w) { n = String(n); while (n.length < (w || 2)) n = "0" + n; return n; }
    var FA = "۰۱۲۳۴۵۶۷۸۹";
    function en(s) { return String(s == null ? "" : s).replace(/[۰-۹]/g, function (c) { return c.charCodeAt(0) - 1776; }).replace(/[٠-٩]/g, function (c) { return c.charCodeAt(0) - 1632; }); }
    function fa(s) { return String(s == null ? "" : s).replace(/\d/g, function (d) { return FA[+d]; }); }

    // «۱۴۰۵/۰۷/۱۹» (هر نوع رقم و جداکننده‌ی / - .) → «2026-10-11»؛ نامعتبر = ""
    function toG(j) {
        var m = /^\s*(\d{4})\s*[\/\-.]\s*(\d{1,2})\s*[\/\-.]\s*(\d{1,2})\s*$/.exec(en(j)); if (!m) return "";
        var jy = +m[1], jm = +m[2], jd = +m[3]; if (jm < 1 || jm > 12 || jd < 1 || jy < 1 || jy > 3000) return "";
        var back = d2j(j2d(jy, jm, jd)); if (back.jy !== jy || back.jm !== jm || back.jd !== jd) return "";   // مثلاً ۳۱ شهریور / ۳۰ اسفندِ سال غیرکبیسه
        var g = d2g(j2d(jy, jm, jd)); return g.gy + "-" + pad(g.gm) + "-" + pad(g.gd);
    }
    // «2026-10-11» یا «2026-10-11T09:30» → «1405/07/19»
    function toJ(g) {
        var m = /^\s*(\d{4})-(\d{2})-(\d{2})/.exec(String(g == null ? "" : g)); if (!m) return "";
        var j = d2j(g2d(+m[1], +m[2], +m[3])); return pad(j.jy, 4) + "/" + pad(j.jm) + "/" + pad(j.jd);   // ارقام لاتین؛ همان‌طور که خود تقویم مقدار را می‌نویسد (با ارقام فارسی نمی‌تواند باز کند)
    }
    function timeOf(g) { var m = /[T ](\d{2}:\d{2})/.exec(String(g == null ? "" : g)); return m ? m[1] : ""; }
    function el(id) { return document.getElementById(id); }
    function attr(s) { return String(s == null ? "" : s).replace(/&/g, "&amp;").replace(/"/g, "&quot;").replace(/</g, "&lt;"); }

    window.mdr.jd = {
        toG: toG, toJ: toJ, en: en, fa: fa, timeOf: timeOf,
        today: function (daysAgo) { var x = new Date(); x.setDate(x.getDate() - (daysAgo || 0)); return x.getFullYear() + "-" + pad(x.getMonth() + 1) + "-" + pad(x.getDate()); },
        L: { time: "ساعت" },
        html: function (id, iso, cls) { return '<input id="' + attr(id) + '" class="' + (cls || "glass-input-ag") + ' pdate" data-jdp jdp-btn-close autocomplete="off" value="' + attr(toJ(iso)) + '">'; },
        timeHtml: function (id, iso, cls) { return '<input id="' + attr(id) + '" type="time" class="glass-time-ag ' + (cls || "glass-input-ag") + '" autocomplete="off" value="' + attr(timeOf(iso)) + '">'; },
        get: function (id) { var e = el(id); return e ? toG(e.value) : ""; },
        set: function (id, iso) { var e = el(id); if (e) e.value = toJ(iso); },
        getDT: function (dateId, timeId, defTime) { var d = this.get(dateId); if (!d) return ""; var t = el(timeId) ? el(timeId).value : ""; t = t || defTime || ""; return t ? d + "T" + t : d; },
        setDT: function (dateId, timeId, iso) { this.set(dateId, iso); if (el(timeId)) el(timeId).value = timeOf(iso); }
    };
    // مقدار نامعتبر (متن آزاد) هنگام ترک فیلد پاک می‌شود تا بی‌صدا نادیده گرفته نشود
    document.addEventListener("focusout", function (e) {
        var t = e.target; if (t && t.classList && t.classList.contains("pdate") && t.value && !toG(t.value)) t.value = "";
    });
})();
