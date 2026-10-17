# BudgetApp

A simple income and expense tracker for everyone — students and people with a job alike. It records what comes in and what goes out, warns you the moment a purchase looks risky, explains where the month's money went, and tells you when you will reach a savings goal at your current pace.

> **Language note:** the user interface is in **Turkish** (currency TL, dates `dd.MM.yyyy`, `tr-TR` culture). The app is designed for **anyone in Turkey who wants to keep track of their money, students and employees included** — the default income categories cover salary, freelance work, part-time jobs, KYK scholarships and family support. Code, identifiers and commit messages are in English.

## Features

- **Income and expense tracking** — amount, category, date, optional item name and note. List, edit and delete, with a month picker (previous / next month) and per-month income, expense and balance totals. Future-dated entries are rejected.
- **Receipt scanning** — take a photo of a shop receipt (the phone's camera opens directly) and Google Gemini reads it: shop, date, every line with a readable product name ("SUT YRM YGL" → "Yarım yağlı süt"), amount and a suggested category, and the receipt total. You check the lines in an editable table, drop the ones you do not want, and each confirmed line is saved as its own expense. The photo is shrunk in the browser, sent once and never stored. A warning appears when the lines do not add up to the total or when the same receipt seems to be saved already; a discount on the whole receipt is spread over the lines. See [Receipt scanning setup](#receipt-scanning-setup).
- **CSV export** — download the shown month as a CSV file that opens correctly in Turkish Excel (UTF-8 with BOM, `;` separator, decimal comma; columns Tarih, Tür, Kategori, Ürün, Not, Tutar). Text that a spreadsheet would run as a formula is defused.
- **Daily allowance** — a card at the top of the dashboard answers "how much can I spend today?": the money on hand, minus the recurring expenses still due before the next recurring income, divided by the days until that income. It is measured at the start of the day, so spending during the day uses the allowance up without shrinking it, and whatever is left (or overspent) carries into tomorrow's. When this month's daily pace would use the available money up before the next income, the card says on which day ("at this pace your money runs out on 27 October").
- **Recurring transactions** — define your salary, rent, subscriptions and other monthly items once; the app adds the transaction on its due day (day 31 falls on the last day of shorter months). Definitions can be paused, resumed, edited or deleted, and a "repeat every month" switch on the new-transaction form creates one from a regular entry. There is no background job: due items are generated when the user opens a page, at most once per month.
- **Savings goal bar** — create a goal (e.g. headphones, 4.500 TL) and watch the bar fill. A "Para ekle" (add money) button on each unfinished goal puts money in by hand; amounts above what the goal still needs are reduced automatically.
- **Month-end carry-over** — in a new month the home page offers to move last month's surplus to the goal with one click (never more than the goal still needs). Money you already added by hand that month is not moved a second time. A month can only be closed once.
- **Instant warnings** — right after an expense is saved, the page shows warnings based on five rules: an unusually large single expense, too much "unnecessary" spending, a category running well ahead of last month, a projected negative month-end balance, and a category budget that is nearly used up or exceeded.
- **Category budgets** — set a monthly limit on an expense category (e.g. coffee, 500 TL). You are warned once when you pass 80% and once when you pass 100% of it each month, and the analysis page shows a progress bar for every limit. Limits are managed on their own small page.
- **Monthly analysis** — Chart.js pie chart by category, the 10 most expensive items, change versus the previous month, and 2–3 rule-based tips.
- **Forecast** — projected month-end balance and "at this pace you reach your goal around *month year*". Recurring income and expenses that are still to come this month are included, so a salary due on the 20th no longer makes the first half of the month look like a deficit.
- **Accounts** — registration and login with ASP.NET Core Identity; every user only ever sees their own data.
- **Dark theme** — follows the operating system by default; a switch in the navbar chooses light, dark or system and the choice is remembered in the browser.

The exact formulas and thresholds are documented in [docs/rules.md](docs/rules.md) and the data model in [docs/model.md](docs/model.md) (both written in Turkish).

## Screenshots

Screenshots go in [docs/screenshots/](docs/screenshots/). Placeholders:

| Screen | File |
| --- | --- |
| Home: goal bar, carry-over card, forecast | `docs/screenshots/home.png` |
| Transactions with month picker and totals | `docs/screenshots/transactions.png` |
| Instant warnings after saving an expense | `docs/screenshots/warnings.png` |
| Monthly analysis with pie chart and tips | `docs/screenshots/analysis.png` |

<!-- Once the images exist, replace the table with e.g. ![Home](docs/screenshots/home.png) -->

## Tech stack

- ASP.NET Core MVC on **.NET 10**
- Entity Framework Core with **SQLite** (a single file, no database server)
- ASP.NET Core Identity for authentication
- Razor views, Bootstrap, and [Chart.js](https://www.chartjs.org/) 4 (vendored under `wwwroot/lib/chartjs`, so the app works offline)
- xUnit for tests

## Architecture

```
src/BudgetApp
├── Models/          Entities (Category, Transaction, Goal, GoalContribution, MonthClose, ReceiptScan) and view models
├── Data/            ApplicationDbContext, EF Core migrations, development demo-data seeder
├── Services/        All business logic: transactions, CSV export, recurring definitions, budgets, goals, month close, warnings, analysis, forecast, daily allowance, receipt reading (Services/Receipts)
├── Infrastructure/  The filter that generates due recurring transactions before each request
├── Controllers/     Thin MVC controllers: read the request, call a service, return a view
├── Views/           Razor views (Turkish UI)
├── Areas/Identity/  Login, register and logout pages
└── Localization/    Turkish Identity and model-binding messages
tests/BudgetApp.Tests    xUnit tests for the services (in-memory SQLite with the real schema)
docs/                    Data model and business rules
```

Design decisions worth knowing:

- **Money is always `decimal`** (`decimal(18,2)` in the database), never `double` or `float`.
- **Controllers stay thin.** Calculations live in `Services`, each of which has tests. Services take "today" as a parameter, so date-dependent rules are tested with a fixed clock.
- **Per-user isolation.** Every query filters by `UserId`; asking for someone else's record behaves like "not found".
- **An LLM only reads receipts.** Receipt scanning is the one place an external API is used, and only to turn a photo into lines the user then checks. The warnings, tips and forecasts are plain, deterministic rules, so every number can be traced back to the data.
- **The receipt reader is replaceable.** `IReceiptParser` hides the provider; `GeminiReceiptParser` calls the REST API with plain `HttpClient` (no SDK package) and a JSON schema for the answer. Its tests use canned HTTP answers and never call the real API.
- **One bold thing per page.** The home page has a single raised surface and a single big number, today's allowance; everything else is a quiet section. Amounts use [Bricolage Grotesque](https://fonts.google.com/specimen/Bricolage+Grotesque) (SIL OFL, bundled under `wwwroot/fonts`) with tabular figures; text stays in Inter. Expense amounts are not red: red is kept for problems. On phones a bottom tab bar replaces the hamburger menu. The design guide is [docs/design-brief.md](docs/design-brief.md).
- **Light and dark theme from one set of variables.** `wwwroot/css/site.css` defines every colour as a CSS variable and redefines them under Bootstrap 5.3's `[data-bs-theme="dark"]`. A few lines of inline script in `<head>` set the theme before the first paint, so the page never flashes in the wrong theme. Each category has a light and a dark tone in `Models/CategoryStyle.cs`; the analysis chart repaints itself when the theme changes.

## Getting started

Requirements: the [.NET 10 SDK](https://dotnet.microsoft.com/download) and Git.

```bash
git clone <repository-url>
cd <repository-folder>

dotnet run --project src/BudgetApp
```

The app listens on `http://localhost:5277` (and `https://localhost:7138`). In the **Development** environment (the default when launched through `dotnet run`) it creates the SQLite database file, applies the migrations and adds a demo account, so no extra setup is needed.

To manage the database manually instead:

```bash
dotnet tool install --global dotnet-ef        # once
dotnet ef database update --project src/BudgetApp
```

Run the tests:

```bash
dotnet test
```

### Receipt scanning setup

Receipt scanning needs a Gemini API key from [Google AI Studio](https://aistudio.google.com/). Keep it out of the code and out of `appsettings.json`:

```bash
dotnet user-secrets set "Gemini:ApiKey" "<your key>" --project src/BudgetApp
```

In production, set the environment variable `Gemini__ApiKey` instead. Without a key, the Development environment runs receipt scanning in **demo mode** (a few made-up receipts, clearly labelled); in any other environment the feature is switched off and its buttons are hidden.

Settings in `appsettings.json`:

| Setting | Default | Meaning |
| --- | --- | --- |
| `Gemini:Model` | `gemini-3.8-flash` | Model used to read receipts |
| `Gemini:TimeoutSeconds` | `60` | How long one receipt may take |
| `Gemini:ThinkingLevel` | `low` | How much the model thinks before answering (`low`, `medium`, `high`; `low` is the lowest `gemini-3.8-flash` accepts). Empty = the model's default |
| `Receipts:DailyLimitPerUser` | `10` | Receipts one user may scan per day (attempts that got no answer, such as timeouts, do not count) |
| `Receipts:DailyLimitTotal` | `200` | Receipts the whole app may scan per day (the free quota belongs to the one key) |
| `Receipts:MaxUploadBytes` | `4000000` | Largest accepted photo |
| `Receipts:MaxImageEdge` | `2048` | The browser shrinks photos to this many pixels on the longer side |

Check the free-tier limits of your key in AI Studio and set the two daily limits below them.

Every Gemini request is logged with its duration, HTTP status, finish reason and token counts (prompt, answer, thinking), e.g. `Gemini gemini-3.8-flash answered HTTP 200 in 6421 ms (thinking low; image 412 KB; finish STOP; tokens: prompt 1290, answer 210, thinking 340)`. The API key is never logged. The log goes to the console of `dotnet run`.

> **Before you offer receipt scanning to others, read the [Gemini API terms](https://ai.google.dev/gemini-api/terms).** In short, as of October 2026:
> - **Free tier data use:** on the unpaid tier Google may use the submitted photos and the answers to improve its products, and human reviewers may read them. Google asks not to send sensitive or personal information. The upload page says this, and suggests covering card numbers and names on the receipt.
> - **Age:** the API may only be used by people 18 or older, and not in apps aimed at or likely to be used by people under 18.
> - **EU, UK and Switzerland:** apps offered to users there may only use the paid tier.

## Demo account

In Development only, a demo user is created on first start with about three months of realistic data. The demo persona happens to be a university student (one example of a user, not the target audience): KYK scholarship and family support, rent, bills, subscriptions, groceries with item names, coffee, transport and entertainment, monthly limits on coffee, eating out, entertainment and groceries (mid-month the analysis page shows all three bar colours), a "Kulaklık" (headphones) goal, and month-close records for the two oldest months. The most recent month is deliberately left open so the carry-over card shows up on the home page.

| | |
| --- | --- |
| E-mail | `demo@budgetapp.local` |
| Password | `Demo123!` |

The monthly items (rent, scholarship, family support, bills, subscriptions, part-time pay) are set up as recurring definitions, so they keep arriving on their own after the first start. The dates are generated relative to the current day, so the data always looks fresh. The demo account is never created outside the Development environment.

## Future improvements

- **More receipt readers** — another `IReceiptParser` (e.g. a different LLM provider or on-device OCR) can be added without touching the pages.
- **LLM-written tips** — keep all numbers computed by the services and let an LLM only phrase the advice sentences more naturally.
- **Mobile app** — a companion app for entering expenses on the go.
- **Change password and profile pages** — currently only registration, login and logout exist; the Identity management pages are not included.
- **E-mail confirmation and password reset** — needs an e-mail sender; accounts are active immediately today.
- **Push notifications** for warnings and month-end reminders.
- **Bank integration** to import transactions automatically.
- **Investment module** for longer-term goals.
