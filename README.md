# 🍰 Lumière — интернет-витрина кондитерской/кофейни

Веб-приложение на **ASP.NET Core 8.0 (MVC)**: витрина с меню и поиском,
корзина, оформление заказа, онлайн-оплата и админ-панель.
База данных не используется — меню хранится в JSON, заказы пишутся в файл,
корзина и вход в админку живут в сессии.

## 📋 Требования

- **[.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)** — нужен, чтобы запускать и собирать проект.

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
- Пароль: `lumiere2024`

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
Для боевых платежей заполни `ShopId` и `SecretKey` в `Lumiere/appsettings.json`.

## 🚀 Деплой на хостинг

В репозитории есть `Dockerfile`, `.dockerignore` и `render.yaml` — этого
достаточно, чтобы развернуть приложение на **Render.com** (New → Web Service,
Runtime: Docker) или на любом другом хостинге с поддержкой Docker.
