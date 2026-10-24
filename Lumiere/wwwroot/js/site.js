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
    catch (e) { document.cookie = 'lumiere_welcome=1; path=/; max-age=31536000'; }

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

// ---------- AJAX: добавление в корзину без перезагрузки ----------
// Делегирование на document — работает и для карточек, подгруженных поиском.
// Не даём странице прыгать наверх: preventDefault + фоновая отправка формы.
document.addEventListener('submit', function (e) {
    var form = e.target;
    if (!form.classList || !form.classList.contains('add-to-cart')) return;
    if (window.LUMIERE_STATIC) return;
    e.preventDefault();

    fetch(form.action, {
        method: 'POST',
        body: new FormData(form),
        headers: { 'X-Requested-With': 'XMLHttpRequest' }
    })
        .then(function (r) { return r.json(); })
        .then(function (res) {
            if (res && res.ok) {
                var badge = document.querySelector('.cart-count');
                if (badge) badge.textContent = res.count;
                var btn = form.querySelector('button[type="submit"]');
                if (btn) {
                    var old = btn.textContent;
                    btn.textContent = 'В корзине ✓';
                    btn.disabled = true;
                    setTimeout(function () { btn.textContent = old; btn.disabled = false; }, 1200);
                }
            }
        })
        .catch(function () { form.submit(); }); // фолбэк: если fetch не удался — обычная отправка
});

// ---------- AJAX: поиск по меню без перезагрузки ----------
// Фокус остаётся в строке поиска, страница не скроллится наверх.
(function () {
    var searchForm = document.getElementById('menu-search');
    if (window.LUMIERE_STATIC) return;
    if (!searchForm) return;

    var input = searchForm.querySelector('input[name="q"]');
    var results = document.getElementById('menu-results');
    var debounce;

    searchForm.addEventListener('submit', function (e) {
        e.preventDefault();
        doSearch();
    });

    if (input) {
        input.addEventListener('input', function () {
            clearTimeout(debounce);
            debounce = setTimeout(doSearch, 350);
        });
    }

    function doSearch() {
        var q = input ? input.value : '';
        var url = '/Home/Menu?q=' + encodeURIComponent(q);
        fetch(url, { headers: { 'X-Requested-With': 'XMLHttpRequest' } })
            .then(function (r) { return r.text(); })
            .then(function (html) {
                results.innerHTML = html;
                // URL обновляем, чтобы ссылку на результаты можно было сохранить/переслать.
                history.replaceState(null, '', q ? url : '/Home/Menu');
            })
            .catch(function () { searchForm.submit(); }); // фолбэк: обычная отправка формы
    }
})();
