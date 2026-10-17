# Bütçe Takip Uygulaması — Sade Plan

|  |  |  |
| --- | --- | --- |
|  |  |  |
|  |  |  |

Oct 7, 2026 · @Eren

## Kapsam

Tek bir ASP.NET Core MVC uygulaması, 2–3 günde, Claude Code ile adım adım. Amaç kusursuz bir ürün değil; GitHub'da temiz duran, açıklayabildiğin, çalışan bir proje.

| Özellik | Nasıl çalışır | Zorluk |
| --- | --- | --- |
| Gelir ve gider girişi | Elle form: tutar, kategori, tarih, isteğe bağlı ürün adı | Kolay |
| Hedef barı | Bir hedef (ör. kulaklık 3.000 TL) ve dolum yüzdesi | Kolay |
| Ay sonu devri | Ay bitince kalan para tek tıkla hedefe aktarılır | Kolay |
| Aylık analiz | Kategori ve ürün bazında harcama, geçen aya göre değişim, grafik | Orta |
| Anında uyarı | Gider kaydedildiği anda kurallara bakıp uyarı gösterir | Orta |
| İleri vade tahmini | "Bu hızla ay sonunda X TL kalır, hedefe Y ayda ulaşırsın" | Kolay |
| Tavsiyeler | Kural tabanlı öneriler; istenirse LLM ile daha doğal metin | Orta |
| Fiş okuma | Fotoğraf yükle, görsel destekli LLM ürünleri çıkarsın, onaylayıp kaydet | Opsiyonel |

**Bilerek dışarıda bırakılanlar:** mobil uygulama, push bildirim, banka entegrasyonu, yatırım modülü, ayrı Python servisi, arka plan işleri. Hepsi README'de "İleride eklenebilecekler" başlığına yazılır; bu da CV'de vizyon gösterir.

## Teknoloji

Her şey tek projede, kurulum gerektirmeyen araçlarla.

| Katman | Seçim |
| --- | --- |
| Çatı | .NET 10 + ASP.NET Core MVC |
| Veritabanı | SQLite + Entity Framework Core (tek dosya, kurulum yok) |
| Giriş | ASP.NET Core Identity (şablondan hazır gelir) |
| Arayüz | Razor + Bootstrap (şablonla gelir) + Chart.js grafikler için |
| Test | xUnit, sadece hesaplama sınıfları için |
| Fiş ve tavsiye metni (opsiyonel) | Görsel destekli bir LLM API'sine .NET'ten doğrudan HTTP çağrısı |

Klasör yapısı da sade: tek bir `BudgetApp` web projesi, içinde `Models`, `Data`, `Services`, `Controllers`, `Views`; yanında `BudgetApp.Tests`. Hesaplama mantığı `Services` altında, controller'lar ince kalır.

## Veri modeli

Beş tablo yeterli. Fişten gelen her ürün satırı ayrı bir `Transaction` olarak kaydedilir; böylece "hangi ürüne ne kadar verdim" analizi ekstra tablo gerektirmez.

| Tablo | Alanlar |
| --- | --- |
| Category | Id, Ad, Tip (gelir/gider), GereksizMi (eğlence, dışarıda yemek gibi kullanıcının işaretlediği kategoriler) |
| Transaction | Id, UserId, Tutar (decimal), Tip, Tarih, CategoryId, ÜrünAdı (boş olabilir), Not |
| Goal | Id, UserId, Ad, HedefTutar, OluşturmaTarihi, TamamlandıMı |
| GoalContribution | Id, GoalId, Tutar, Tarih, Kaynak (ay sonu devri / elle) |
| MonthClose | Id, UserId, Yıl, Ay, KalanTutar, DevredilenGoalId |

`MonthClose` aynı ayın iki kez devredilmesini engeller. Para alanları her zaman `decimal`.

## Kurallar

Uygulamanın "akıllı" görünen kısmı aslında birkaç basit kuraldan oluşuyor. Hepsi `Services` altında, test edilebilir sınıflar.

### Ay sonu devri

Yeni ayın ilk girişinde, geçen ay için `MonthClose` kaydı yoksa ekranda bir kart çıkar: "Ekim'de 840 TL artırdın. Kulaklık hedefine aktaralım mı?" Onaylanırsa `GoalContribution` oluşur ve bar dolar. Kalan negatifse aktarım yapılmaz, sadece not düşülür.

### Anında uyarılar

Gider kaydedildiği anda `WarningService` şu kurallara bakar ve uyarıyı sayfanın üstünde gösterir:

1. Tek harcama, bu ayki günlük ortalama harcamanın 3 katından büyükse.
2. "Gereksiz" işaretli kategorilerin bu ayki toplamı, aylık gelirin %20'sini geçtiyse.
3. Bir kategori, geçen ayın aynı gününe göre %30'dan fazla öndeyse.
4. Mevcut harcama hızıyla ay sonunda eksiye düşülecekse.

Uyarı metinleri samimi olabilir: "Bu ay dışarıda yemeğe 1.200 TL gitti, geçen ayın iki katı. Bu hızla ay sonunda menüde makarna var."

### Aylık analiz ve tavsiye

Analiz sayfası kategori pastası, ürün bazında ilk 10 harcama ve geçen ayla karşılaştırma gösterir. Altında 2–3 tavsiye çıkar:

- Kural tabanlı: "Kahveye ayda 900 TL veriyorsun; yarıya indirsen hedefine 2 ay erken ulaşırsın."
- Opsiyonel LLM: aylık özet JSON olarak LLM'e verilir, LLM sadece tavsiye cümlelerini yazar. Rakamlar her zaman kendi hesabından gelir.

### İleri vade tahmini

```latex
\text{Tahmini ay sonu kalanı} = \text{Bu ayki gelir} - \text{Bugüne kadarki gider} \times \frac{\text{Ayın gün sayısı}}{\text{Geçen gün}}
```

Hedef süresi = kalan hedef tutarı ÷ son 3 ayın ortalama devri. Ana sayfada tek cümle: "Bu tempoyla kulaklığa Şubat'ta ulaşırsın."

## Adımlar

Beş adım, 2–3 güne dağılıyor: 1. gün Adım 1 ve 2; 2. gün Adım 3 ve 5 (bu noktada proje GitHub'a hazır); 3. gün zaman kalırsa Adım 4 (fiş okuma) ve README güncellemesi. Her adımın sonunda uygulama çalışır ve commit atılmış olur; 3. adımda bıraksan bile GitHub'a koyulacak bir proje vardır.

| Adım | Ne çıkar | Ne öğrenirsin |
| --- | --- | --- |
| 1. Kurulum ve gelir/gider | Giriş, kategori, işlem ekleme/listeleme | MVC akışı, EF Core, migration, Identity |
| 2. Hedef barı ve ay sonu devri | Hedef oluşturma, dolum barı, devir kartı | Servis katmanı, ilişkili tablolar, birim testi |
| 3. Analiz ve uyarılar | Grafikli analiz sayfası, anında uyarılar, tahmin | LINQ ile gruplama, Chart.js, iş kuralı yazmak |
| 4. Fiş okuma (opsiyonel) | Fotoğraftan ürün listesi, onay ekranı | Dış API çağrısı, JSON, gizli anahtar yönetimi |
| 5. Cilalama ve GitHub | README, ekran görüntüleri, örnek veri | Dokümantasyon, proje sunumu |

### Claude Code komutları

Her adımda önce plan iste, onayla, sonra uygulat. Örnekler:

- **Adım 1:** "Bireysel hesaplı yeni bir .NET 10 ASP.NET Core MVC projesi kur, SQLite kullan. docs/model.md'deki Category ve Transaction modellerini ekle, migration oluştur, varsayılan kategorileri seed et. İşlem ekleme, listeleme, düzenleme, silme sayfalarını yap. Önce planını göster."
- **Adım 2:** "Goal, GoalContribution ve MonthClose modellerini ekle. Ay sonu devrini MonthCloseService içinde yaz. Önce bu servis için xUnit testlerini yaz, ben onaylayınca implementasyona geç."
- **Adım 3:** "WarningService'i docs/rules.md'deki 4 kurala göre yaz, her kural için test olsun. Gider kaydedildikten sonra uyarıyı TempData ile listede göster. Sonra Chart.js ile aylık analiz sayfasını yap."
- **Adım 4:** "Fiş fotoğrafı yükleme sayfası ekle. Görseli LLM API'sine gönder, ürün adı, miktar ve fiyatı JSON olarak al, kullanıcıya düzenlenebilir tablo olarak göster, onaylanan satırları Transaction olarak kaydet. API anahtarı user-secrets'tan okunsun."
- **Adım 5:** "README.md yaz: proje amacı, özellikler, kullanılan teknolojiler, kurulum adımları, ekran görüntüsü yerleri, ileride eklenebilecekler. Bir de demo kullanıcısı için 3 aylık örnek veri seed'i ekle."

## 

### Kısa CLAUDE.md

```markdown
# Yokla (kod adı BudgetApp)
Öğrenciler ve çalışanlar dahil herkes için gelir-gider takip uygulaması. ASP.NET Core MVC, .NET 10, EF Core, SQLite.

## Kurallar
- Para her zaman decimal.
- Hesaplama mantığı Services altında; controller'lar ince.
- Services altındaki her public metodun testi olsun.
- Yeni paket eklemeden önce sor.
- API anahtarları user-secrets'ta; koda yazma.
- Arayüz Türkçe; kod ve commit mesajları İngilizce.
- Her görev sonunda dotnet build ve dotnet test çalıştır.
```

### README'de olması gerekenler

- Bir cümlelik açıklama ve ana ekranın GIF'i
- Özellik listesi (ay sonu devri, anında uyarılar, tahmin, fiş okuma)
- Kullanılan teknolojiler ve kısa mimari açıklaması
- Kurulum: `git clone`, `dotnet ef database update`, `dotnet run`
- İleride eklenebilecekler

## 1. gün

- [ ] GitHub'da boş repo aç (`budget-app` gibi)
- [ ] Proje köküne CLAUDE.md'yi koy
- [ ] `docs/model.md` ve `docs/rules.md` dosyalarına bu dokümandaki tabloları ve kuralları kopyala
- [ ] Adım 1 komutunu Claude Code'a ver, planı oku, onayla
- [ ] Uygulamayı çalıştır, birkaç gerçek harcamanı gir
- [ ] İlk commit ve push
