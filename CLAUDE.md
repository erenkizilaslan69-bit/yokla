# BudgetApp

Öğrenciler ve çalışanlar dahil herkes için gelir-gider takip uygulaması.
Teknoloji: ASP.NET Core MVC, .NET 10, Entity Framework Core, SQLite, ASP.NET Core Identity, Bootstrap, Chart.js.

Detaylar:
- docs/model.md — veri modeli
- docs/rules.md — ay sonu devri, uyarı kuralları, analiz ve tahmin formülleri

## Komutlar
- Build: `dotnet build`
- Test: `dotnet test`
- Çalıştır: `dotnet run --project src/BudgetApp`
- Migration: `dotnet ef migrations add <Ad> --project src/BudgetApp` ve `dotnet ef database update --project src/BudgetApp`

## Kurallar
- Para her zaman `decimal`; `double` veya `float` kullanma.
- Hesaplama ve iş mantığı `Services` altında; controller'lar ince kalsın.
- `Services` altındaki her public metodun xUnit testi olsun (`tests/BudgetApp.Tests`).
- Her kullanıcı sadece kendi verisini görür; sorgular her zaman `UserId` ile filtrelenir.
- Yeni NuGet paketi eklemeden önce sor.
- API anahtarları `dotnet user-secrets` ile tutulur; koda veya appsettings.json'a yazılmaz.
- Arayüz metinleri Türkçe; kod, değişken adları ve commit mesajları İngilizce.
- Para birimi TL, tarih biçimi gg.aa.yyyy (tr-TR kültürü).
- Bir görev bitince `dotnet build` ve `dotnet test` çalıştır, sonucu raporla.
- Testleri geçirmek için testleri gevşetme veya silme; test yanlışsa söyle.
- Önce plan sun, onay gelmeden büyük değişiklik yapma.
