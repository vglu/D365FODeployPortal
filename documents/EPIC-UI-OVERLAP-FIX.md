# EPIC-UI-OVERLAP-FIX — «Чтобы перестало наезжать»

**Owner draft:** Алиса (UX, голос пользователя)
**Wave breakdown:** Анатолий (operations, требования к масштабу)
**Status:** W1 ✅ done · W2 ✅ done · W3 ✅ done (W3.6 documented, deferred for visual review) · awaiting browser smoke-test
**Created:** 2026-05-02

---

## 🩹 Боль (от лица Алисы)

Я открываю Deploy Portal на ноутбуке 13" и **первое, что вижу** — что элементы интерфейса наезжают друг на друга. Особенно когда я работаю с master-detail (раскрытый Merged-пакет, страница с деталями деплоя, выбор пакета+окружения для деплоя). Не падает, не ломается — но **выглядит как «у нас здесь не доделано»**, и я каждый раз трачу секунду, чтобы понять, где заканчивается одна область и начинается другая.

Конкретные сценарии, в которых я спотыкаюсь:

1. **Боковое меню**: внизу drawer'а есть футер с версией и ссылкой на SIMS tech. На моём экране он **закрывает пункт Settings**. Я кликаю по «Settings», а попадаю на ссылку SIMS tech и улетаю на сайт.
2. **Страница Deploy**: две таблицы рядом — пакеты и окружения. Каждая ровно 460 пикселей высоты, **независимо от моего экрана**. На широком 4K мониторе это маленькие окошки в океане белого; на ноуте — съедают полстраницы и кнопка Deploy внизу не помещается без скролла.
3. **Раскрыл Merged-пакет на странице Packages** — увидел список «Merged from: A, B». Но визуально этот блок **выпадает из таблицы** (margin создаёт зазор), и я не могу понять — это часть строки выше или новая строка.
4. **DeploymentDetail**: открываю историю деплоя — слева табличка с инфой, **справа пустота**, под ней лог в чёрном фиксированном окошке 500px. Я думаю, что страница не догрузилась. И ещё лог чёрный даже когда у меня светлая тема приложения.
5. **Лог во время деплоя** растёт неограниченно — на больших пакетах через минуту-две браузер тормозит, и я не могу промотать к свежим строкам, потому что они снизу, а скролл я уже потерял.

## 🎯 Цель эпика

После этого эпика я хочу:

- **Открыть приложение на любом экране и не видеть наезжающих элементов.** Каждая зона имеет чёткую границу, master-detail не требует «чинить взглядом».
- **Понимать, какой паттерн master-detail используется на конкретной странице** и не учить три разных каждый раз.
- **Видеть live-логи без лагов** даже на длинных деплоях.
- **Чувствовать, что dark/light mode реально работает** — переключатель не оставляет «островков чёрного» в светлой теме.

## ✅ Success criteria (как я пойму, что готово)

| Сценарий | Текущее поведение | Целевое поведение |
|---|---|---|
| Drawer с футером, экран 720px высоты | Футер закрывает «Settings» | Футер всегда ниже последнего пункта; никогда не перекрывает меню |
| Deploy.razor на 1080p | Таблицы 460px, кнопка Deploy под фолдом | Таблицы занимают доступную высоту, кнопка Deploy всегда видна без скролла |
| Deploy.razor на 720p ноуте | Те же 460px, тесно | Таблицы адаптируются (~min 280px), на узких — колонки сворачиваются |
| Раскрытие Merged-пакета | MudPaper «выпадает» из строки | Детский блок визуально приклеен к родительской строке (без зазора, есть индикатор связи) |
| DeploymentDetail в light mode | Лог чёрный, правая колонка пустая | Лог использует тему; правая колонка не пустует (или layout перестроен) |
| 5000+ строк в логе | Браузер лагает, нужно скроллить руками | Виртуализация + auto-scroll к низу с pin-to-bottom переключателем |

## 🚫 Out of scope (для этого эпика)

- Полный редизайн навигации (это отдельный эпик)
- Stepper в Deploy / wizard-режим (фича, не баг — отдельный эпик)
- Сохранение пресетов окружений (отдельный эпик)
- Repeat deployment / copy-as-curl (отдельный эпик)
- Полный a11y-аудит с WCAG (отдельный эпик)

## 🔗 Зависимости

- Никаких внешних сервисов / API не трогаем
- БД-миграции не нужны
- Никаких изменений в публичных REST-контрактах `/api/packages/*`
- Совместимо с существующими версиями MudBlazor 8.x

---

## 🌊 Waves (от лица Анатолия)

Я работаю с этим инструментом каждый день, у меня 50+ деплоев в неделю. Я не готов ждать «большой релиз через месяц» — мне нужно **чтобы перестало наезжать уже завтра**, а более глубокие штуки можно потом.

Поэтому режу так: **W1 чинит видимое за 1 день, W2 укрепляет основу за 2-3 дня, W3 косметика без давления.**

### 🔴 Wave 1 — «Стоп наезд» (P0, ~1 рабочий день)

Цель: **закрыть жалобу пользователя в текущей итерации.** Никакой архитектуры, только точечные правки в существующих файлах.

| ID | Задача | Файл | Оценка | Доказательство готово |
|---|---|---|---|---|
| W1.1 | Drawer footer overlap: вынести из `position:absolute` в flex-обёртку | `Components/Layout/NavMenu.razor:24` | 30 мин | Окно 600px высоты — последний пункт меню кликабелен, футер ниже него или скроллится |
| W1.2 | Deploy.razor: 460px → calc-based + min-height; вернуть Breakpoint.Md где было None | `Components/Pages/Deploy.razor:29-94` | 1 ч | На 1080/1440/720 видны и таблицы, и кнопка Deploy без скролла |
| W1.3 | Packages.razor: убрать падение MudPaper из строки, дать визуальный якорь | `Components/Pages/Packages.razor:224-256` | 1 ч | Раскрытый Merged выглядит частью строки, без зазора |
| W1.4 | DeploymentDetail.razor: тематизация лог-блока + правая колонка не пустует | `Components/Pages/DeploymentDetail.razor:25-89` | 1.5 ч | В light mode лог светлый или явно «code surface», правая колонка либо заполнена, либо grid перестроен |
| W1.5 | Build + smoke-проверка | вся солюшн | 30 мин | `dotnet build` зелёный, app поднимается, все 7 страниц рендерятся без ошибок в консоли |

**Definition of Done для W1:**
- `dotnet build` без новых варнингов
- На viewport'ах 1920×1080, 1440×900, 1366×768, 1024×768 нет видимых перекрытий на главных экранах (Home, Packages, Deploy, Deployments, DeploymentDetail, Environments, Settings)
- Тёмная и светлая темы работают одинаково корректно на DeploymentDetail
- Не сломан ни один существующий тест в `src/DeployPortal.Tests/`

### 🟡 Wave 2 — «Сделать предсказуемым» (P1, ~3 рабочих дня)

Цель: **починить причину**, а не симптом. Привести master-detail к единому паттерну, добавить виртуализацию, заложить дизайн-токены.

| ID | Задача | Файл | Оценка |
|---|---|---|---|
| W2.1 | Виртуализация лог-вьювера через `Virtualize<DeploymentLog>` + auto-scroll-to-bottom toggle | `DeploymentDetail.razor` | 4 ч |
| W2.2 | Принять решение о master-detail policy и привести `DeploymentDetail` к нему (вариант A: side-drawer; вариант B: симметричный grid) | `DeploymentDetail.razor`, `Deployments.razor` | 6 ч |
| W2.3 | `wwwroot/css/tokens.css` с CSS-переменными для редких кейсов вне `var(--mud-palette-*)` (font sizes, code surface, log-level colors) | новый файл + рефакторинг inline-стилей | 4 ч |
| W2.4 | Привести цветовую семантику: `Color.Secondary` для «Merge» → `Color.Primary`; убрать `Color.Tertiary`; единый `GetStatusColor`-helper в shared utility | `Packages.razor`, `DeploymentDetail.razor`, `Deployments.razor` | 3 ч |
| W2.5 | Убрать инлайновые `font-size: 0.7rem/0.78rem/...` в пользу `Typo.caption` или одной CSS-переменной `--code-fz` | глобальный поиск-замена | 2 ч |

**Definition of Done для W2:**
- Лог-вьювер не лагает на 10k строк (тест в Tests-проекте)
- Master-detail паттерн один и тот же на трёх страницах (или решение задокументировано)
- Все цвета в коде — либо `Color.*` enum, либо `var(--mud-palette-*)`. Никаких `#1e1e1e/#888/#ddd` в .razor файлах
- Обновлён `documents/UI-PATTERNS.md` (новый файл) с описанием паттерна master-detail

### 🟢 Wave 3 — «Полировка» (P2, ~2-3 рабочих дня, по желанию)

Цель: **повысить tactile feel** без давления.

| ID | Задача | Оценка |
|---|---|---|
| W3.1 | Outlined-вариант для secondary-action иконок (Edit / Search / Settings nav) | 2 ч |
| W3.2 | Stepper в Deploy.razor вместо «1./2.» (или явный non-stepper) | 4 ч |
| W3.3 | Skip-to-content link в AppBar для keyboard-юзеров | 1 ч |
| W3.4 | `aria-selected` / role=radiogroup на single-select таблицах (Deploy → пакет) | 2 ч |
| W3.5 | Tooltip-консистентность: либо везде на датах, либо нигде | 2 ч |
| W3.6 | Стандартизировать spacing на 3 значения (`pa-2/pa-4/pa-6`) — глобальная замена | 4 ч |

**Definition of Done для W3:**
- Lighthouse a11y score ≥ 90 на главных страницах
- Дизайн-критика повторно прогнана — ни одного «inconsistent» вердикта в spacing/type/color

---

## 📐 Один смоук-тест

Запустить `dotnet watch --project src/DeployPortal --urls "http://localhost:5137"`, открыть в окне ровно **1366×768** (типичный ноут). Пройти путь: Home → Packages → раскрыть Merged-строку → Deploy → выбрать пакет+2 окружения → Deploy → /deployments → DeploymentDetail. **На каждом шаге не должно быть ни одного видимого перекрытия и ни одного фиксированного блока, который не помещается в viewport.**

После Wave 1 этот тест должен проходить.

---

## 📎 Связанные эпики (потенциальные)

- EPIC-DEPLOY-PRESETS — сохранение «обычная неделя» наборов окружений
- EPIC-DEPLOY-REPEAT — Repeat deployment / copy-as-curl
- EPIC-NAV-REDESIGN — пересмотр главной навигации (Dashboard как hub-страница с CTA)
- EPIC-A11Y-WCAG — полный a11y аудит

---

## 📋 Changelog (что реально сделано)

### 2026-05-02 — Wave 1 + Wave 2 + Wave 3

**Прелюдия (критический фикс):**
- **W2.0** — `app.css` не подгружался в `App.razor` вообще. Добавлены `<link href="app.css">` и `<link href="css/tokens.css">`. Без этого все W1 CSS-правки не применялись.

**Wave 1 — «Стоп наезд»:**
- **W1.1** — drawer footer вынесен в flex-обёртку `.nav-menu-flex` (NavMenu.razor + app.css). Больше не overlap'ит «Settings» на коротких viewport'ах.
- **W1.2** — Deploy.razor table heights: `460px` → `clamp(280px, calc(100vh - 320px), 720px)`. `Breakpoint.None` → `Breakpoint.Sm`.
- **W1.3** — Packages.razor master-detail: `.master-detail-row` CSS-класс с inset-фоном и левой акцент-полосой; убран `padding:0` + `ma-2` глюк.
- **W1.4** — DeploymentDetail.razor: симметричный grid (info 7/12 + Summary card 5/12, правая половина больше не пустует); лог через `.code-surface` тематизирован под `--mud-palette-*` (работает в light/dark).
- **W1.5** — `dotnet build Project4.sln`: 0 warnings, 0 errors.

**Wave 2 — «Сделать предсказуемым»:**
- **W2.1** — Лог-вьювер: `<Virtualize>` + JS auto-scroll через `wwwroot/js/log-scroll.js` (`requestAnimationFrame` для корректного scrollHeight после layout). Auto-scroll respects manual scroll (если пользователь прокрутил вверх — не дёргаем).
- **W2.2** — Master-detail policy: `documents/UI-PATTERNS.md` с тремя вариантами (inline / route / dialog) и критериями выбора. DeploymentDetail.razor оставлен в W1.4-варианте (route + symmetric grid) как канонический пример «route».
- **W2.3** — `wwwroot/css/tokens.css` с CSS custom properties (`--dp-fz-chip-meta`, `--dp-fz-table-aux`, `--dp-fz-code`, `--dp-font-mono`) + утилитарные классы `.dp-text-chip-meta`, `.dp-text-table-aux`, `.dp-font-mono`, `.code-block`.
- **W2.4** — Цветовая семантика: ~40 точек `Color.Secondary`/`Color.Tertiary` приведены к `Default`/`Primary`/`Info`/`Warning` по правилам из UI-PATTERNS.md. Merge action → Primary; "Merged" package type → Warning. Color.Tertiary полностью удалён из non-timeline кода.
- **W2.5** — Inline `font-size:` убраны (~10 точек) в пользу токен-классов. Code-blocks (Home, Environments setup-script paper) переведены на `.code-block`.

**Wave 3 — «Полировка»:**
- **W3.1** — Outlined-варианты для secondary actions: `Filled.Search` → `Outlined.Search` (5 файлов), `Filled.Edit` → `Outlined.Edit` (2 файла), `Filled.Refresh` → `Outlined.Refresh` (3 файла). Destructive (Delete, Cancel) и menu-trigger (MoreVert) оставлены Filled.
- **W3.2** — Stepper в Deploy.razor: вместо введения MudStepper убраны wizard-ные «1.» / «2.» (они вводили в заблуждение — селекторы параллельные, не sequential). Заголовки теперь "Select Package" / "Select Environments".
- **W3.3** — Skip-to-content link в MainLayout.razor + `.skip-link` в app.css. Visually-hidden до фокуса, при Tab появляется в верхнем-левом углу.
- **W3.4** — `role="radiogroup"` + `aria-label` на пакет-таблицу в Deploy.razor; `<span class="visually-hidden">Selected: </span>` на выбранную строку. Добавлен `.visually-hidden` utility в app.css.
- **W3.5** — MudTooltip с полной датой (yyyy-MM-dd HH:mm:ss) добавлен на 9 датовых ячеек (Deployments active+archive, Packages archive). Лог-таймстампы в DeploymentDetail оставлены без tooltip — на тысячах виртуализованных строк tooltip-обёртки бы убили DOM.
- **W3.6** — Стандартизация spacing: blanket-replace 89 точек off-canon признан рисковым без визуального ревью. Вместо этого UI-PATTERNS.md обновлён с правилом «primary 2/4/6, tolerated transitional 3, avoid 5/7». Будущие PR конвергируют к канону при касании.

### Артефакты
- `documents/EPIC-UI-OVERLAP-FIX.md` — этот файл
- `documents/UI-PATTERNS.md` — UI-policy документ
- `wwwroot/app.css` — flex drawer, master-detail, code-surface, log-line, skip-link, visually-hidden
- `wwwroot/css/tokens.css` — design-tokens + utility-классы
- `wwwroot/js/log-scroll.js` — JS-helper для auto-scroll лога

### Build
- `dotnet build Project4.sln` — 0 warnings, 0 errors (5 проектов)
