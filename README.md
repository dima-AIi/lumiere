# 🍰 Lumière — интернет-витрина кондитерской/кофейни

Веб-приложение на **ASP.NET Core 10 (MVC)**: витрина с меню и поиском,
корзина, оформление заказа, онлайн-оплата и админ-панель.
Публичные страницы собираются для Vercel. Сервер работает на Render, а меню,
заказы, загруженные JPEG, сессии и ключи cookies хранятся в Neon PostgreSQL.
Локально без DATABASE_URL сохраняется файловый режим.

## 📋 Требования

- **[.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)** — нужен, чтобы запускать и собирать проект.

## ▶️ Как запустить на своём компьютере

1. (Если переносил архив) распакуй папку `Lumiere`.
2. Открой терминал **PowerShell** в папке проекта `Lumiere`:
   ```powershell
   cd "путь\к\папке\Lumiere"
   ```
3. Выполни:
   ```powershell
   dotnet run
   ```
4. Дождись строки `Now listening on: http://localhost:5055`
5. Открой в браузере: **http://localhost:5055**

### 🔐 Админ-панель
- Адрес: **http://localhost:5055/Admin**
- Пароль задаётся переменной окружения `Admin__Password`. Встроенного пароля нет.

## 📦 Перенос на другой компьютер (ZIP)

1. Создать чистый архив из репозитория (без `bin/`, `obj/`, `node_modules/`):
   ```powershell
   git archive --format=zip --prefix=Lumiere/ -o Lumiere-portable.zip HEAD
   ```
2. Перенеси `Lumiere-portable.zip` на другой ПК и распакуй.
3. Установи **.NET 8 SDK** и запусти, как описано выше (`dotnet run`).

> Заказы (`App_Data/orders.json`) и изменения меню создаются автоматически при
> первом запуске. Если файла заказов нет — приложение начинает с пустого списка.

## 🗂️ Структура проекта

| Путь | Что внутри |
|---|---|
| `Lumiere/Controllers` | `HomeController` (витрина, чекаут, оплата), `AdminController` |
| `Lumiere/Models` | модели меню и заказов |
| `Lumiere/Services` | `MenuRepository`, `CartService`, `OrderStore`, `YooKassaService` |
| `Lumiere/Views` | Razor-представления (Home, Admin, Shared) |
| `Lumiere/wwwroot` | статика: css, js, картинки, `data/menu.json` |
| `Lumiere/App_Data` | файл заказов `orders.json` (создаётся при запуске, в git не хранится) |

## 💳 Онлайн-оплата

Подключена **ЮKassa** в демо-режиме (ключи пустые в `appsettings.json`).
Для реальных платежей задайте `YooKassa__ShopId`, `YooKassa__SecretKey` и
`YooKassa__ReturnUrl` в настройках окружения Render. Секреты не записывайте в Git.

## ▶️ Запуск

Локально — `dotnet run` (см. выше). Публикация, переменные, миграции и проверки
описаны в [DEPLOYMENT.md](DEPLOYMENT.md). Конфигурации: `vercel.json`,
`render.yaml`, `Dockerfile`. Все ресурсы Lumiere создаются отдельно.
