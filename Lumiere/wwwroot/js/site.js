// Please see documentation at https://learn.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

// Видео: форсируем автозапуск без звука (требование браузеров).
document.querySelectorAll('video[autoplay]').forEach(function (v) {
    v.muted = true;
    var p = v.play();
    if (p && p.catch) p.catch(function () { });
});

// Приветственный прелоадер: только 1 раз — при первом визите на сайт.
// Решение «показывать или нет» принимает скрипт в <head> (класс show-welcome у <html>):
// если его нет — экран даже не рисуется, поэтому переходы по сайту его не трогают.
(function () {
    var pre = document.getElementById('preloader');
    if (!pre) return;

    if (!document.documentElement.classList.contains('show-welcome')) {
        pre.style.display = 'none';
        return;
    }

    // Запоминаем, что приветствие показано (для повторного показа — /?welcome=1).
    try { localStorage.setItem('lumiere_welcome', '1'); }
    catch (e) { /* Storage may be unavailable; no cookie fallback. */ }

    var MIN_MS = 1600; // время прочитать приветствие
    var start = Date.now();
    var hidden = false;
    function hide() {
        if (hidden) return;
        var left = MIN_MS - (Date.now() - start);
        if (left > 0) { setTimeout(hide, left); return; }
        hidden = true;
        pre.classList.add('hide');
        setTimeout(function () { pre.style.display = 'none'; }, 500);
    }

    if (document.readyState === 'complete') hide();
    window.addEventListener('load', hide);
    setTimeout(hide, 5000); // страховка: если ресурс завис — не блокируем страницу
    window.addEventListener('pageshow', function (e) { if (e.persisted) hide(); });
})();


const burger = document.getElementById('burger');
const nav = document.getElementById('nav');
burger?.addEventListener('click', () => nav?.classList.toggle('open'));
