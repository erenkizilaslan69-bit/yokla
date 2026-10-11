# İş kuralları

## 1. Ay sonu devri (MonthCloseService)
- Kullanıcı yeni ayda uygulamayı açtığında, geçen ay için MonthClose kaydı yoksa ana sayfada bir kart gösterilir.
- Kalan = geçen ayın gelirleri − geçen ayın giderleri. `MonthClose.Remaining` her zaman bu gerçek kalanı saklar.
- **Zaten eklenen** = o ay içinde (ayın 1'i–son günü) kullanıcının herhangi bir hedefine elle eklediği (Source = Manual) katkıların toplamı. MonthClose kaynaklı katkılar sayılmaz.
- **Aktarılabilir tutar** = max(0, Kalan − Zaten eklenen). Elle eklenen para gider olarak kaydedilmediği için kalanın içinde görünür; aynı para ay sonunda ikinci kez aktarılmasın diye düşülür. Aktarılan tutar ayrıca hedefin eksik tutarıyla sınırlıdır.
- Aktarılabilir tutar > 0 ve aktif bir hedef varsa:
  - Zaten eklenen yoksa: "Geçen ay {Kalan} TL artırdın. {Hedef} hedefine aktaralım mı?"
  - Zaten eklenen varsa: "{Ay} ayında {Kalan} TL artırdın; {Zaten eklenen} TL'sini zaten hedefe eklemiştin. Kalan {Aktarılabilir} TL'yi aktaralım mı?"
  - Onay: aktarılabilir tutar kadar GoalContribution (Source = MonthClose) oluşur, MonthClose.TransferredGoalId dolar.
  - Ret: MonthClose kaydedilir, aktarım yapılmaz.
- Kalan > 0 ama aktarılabilir tutar 0 ise (elle eklenenler kalanı karşılıyor): aktarma teklifi çıkmaz, bilgi mesajı gösterilir ("{Ay} ayında {Kalan} TL artırdın; {Zaten eklenen} TL'sini zaten hedefe eklemiştin. Aktarılacak tutar kalmadı."). Ay yine MonthClose ile kapatılır, aktarım yapılmaz.
- Kalan ≤ 0: MonthClose kaydedilir, aktarım yapılmaz, kartta "Geçen ay {|Kalan|} TL açık verdin" yazar.
- Aynı ay iki kez kapatılamaz.

### Tamamlanan hedef için kutlama kartı (GoalService.GetCelebrationGoalAsync)
- Ana sayfada aktif (tamamlanmamış) hedef yoksa ve kullanıcının en son tamamlanan hedefi son 30 gün içinde (30. gün dahil) tamamlandıysa hedef kartı yerine kutlama kartı gösterilir: hedefin adı, tutarı, tamamlanma tarihi ve "Yeni hedef oluştur" düğmesi.
- En son tamamlanan hedef 30 günden eskiyse (veya hiç hedef yoksa) normal boş durum ekranı gösterilir. Yeni bir aktif hedef oluşturulunca kutlama kartı kaybolur.
- Tamamlanma tarihi (`Goal.CompletedAt`) hedefi tamamlayan katkının tarihidir (elle ekleme için o gün, ay sonu devri için devrin yapıldığı gün).

### Hedefe elle para ekleme (GoalService.AddManualContributionAsync)
- Hedefler sayfasındaki ve ana sayfadaki hedef kartında, tamamlanmamış her hedef için "Para ekle" formu bulunur.
- Tutar 2 ondalığa yuvarlanır ve 0'dan büyük olmalıdır.
- Tutar hedefin eksik tutarını (hedef − tüm katkılar) aşarsa eksik tutarla sınırlanır ve kullanıcıya bildirilir; hedef tamamlanır.
- Katkı Source = Manual ile kaydedilir. Tamamlanmış hedefe para eklenemez.
- Bu katkılar ay sonu devrinde yukarıdaki "zaten eklenen" tutarı oluşturur.

## 2. Anında uyarılar (WarningService)
Gider kaydedildikten hemen sonra çalışır, tetiklenen uyarılar sayfanın üstünde gösterilir.

1. **Büyük harcama:** Tek harcama > bu ayki günlük ortalama değişken giderin 3 katı. (Ayın ilk 3 gününde bu kural çalışmaz.)
   - Günlük ortalama = bu ayki değişken giderler (kaydedilen harcama hariç) ÷ geçen gün sayısı. Ortalama 0 ise kural çalışmaz.
   - Sabit gider kategorilerindeki (IsFixed) harcamalar için kural çalışmaz (kira gibi beklenen ödemeler uyarı üretmez).
   - Tekrarlayan tanımdan oluşan giderler de sabit gider gibi sayılır: kuralı tetiklemez ve ortalamaya girmez (bkz. 5).
2. **Gereksiz harcama payı:** IsUnnecessary kategorilerin bu ayki toplamı > bu ayki toplam gelirin %20'si.
3. **Kategori artışı:** Bir kategorinin bu ayın bugününe kadarki toplamı > geçen ayın aynı gününe kadarki toplamının %130'u. (Geçen ay o kategoride harcama yoksa çalışmaz.)
4. **Eksiye düşme riski:** Tahmini ay sonu kalanı (bkz. 4) < 0. Bu ay bilinen gelir yoksa çalışmaz (gelir girilmeden her harcama "açık" görünürdü). Bilinen gelir = kayıtlı gelir + bu ay henüz gelmemiş tekrarlayan gelir.

5. **Kategori bütçesi:** Kaydedilen giderin kategorisinde kullanıcının aylık limiti varsa ve kategorinin bu ayki toplamı (otomatik oluşan tekrarlayan giderler dahil) limitin %80'ini geçtiyse "yaklaşıyorsun", %100'ünü geçtiyse "aştın" uyarısı çıkar (bkz. 6). Tam %80 ve tam %100 sınırın içindedir: %80'de uyarı yok, %100'de hâlâ "yaklaşıyorsun".

Kural 1 yalnızca gider, kural 2–5 kaydedilen giderin kategorisine göre çalışır (kural 2 sadece gereksiz kategorideki harcamada, kural 3 sadece o kategori için). Uyarılar yalnızca bu ay tarihli işlemler için hesaplanır.

Kural 1–4 hesaplanır ve gösterilir, hiçbir şey saklamaz. Kural 5 ise hatırlar: bir bütçe seviyesi aynı ay içinde bir kez bildirilir (bkz. 6), bu yüzden `WarningService.CheckAsync` bütçe satırını günceller.

Uyarılar kullanıcının kendi kaydettiği giderde çalışır. Tekrarlayan tanımdan otomatik oluşan işlemlerde çalışmaz; onlar için tek bir "şu işlemler eklendi" bildirimi gösterilir (bkz. 5).

Uyarı metinleri samimi ve kısa olsun, her zaman gerçek rakam içersin. Örnek:
"Bu ay dışarıda yemeğe 1.200 TL gitti, geçen ayın iki katı. Bu hızla ay sonunda menüde makarna var."

## 3. Aylık analiz (AnalysisService)
- Kategori bazında toplam gider (pasta grafik).
- ItemName bazında en çok harcanan 10 ürün (sabit gider kategorileri — IsFixed — ve tekrarlayan tanımdan oluşan giderler hariç).
- Geçen aya göre kategori değişimi (TL ve %).
- 2–3 kural tabanlı tavsiye, örneğin:
  - En çok artan gereksiz kategori için: "{Kategori} harcamanı yarıya indirsen hedefine {N} ay erken ulaşırsın."
  - Sık tekrar eden ürün için: "{Ürün} için bu ay {Adet} kez toplam {Tutar} TL ödedin."
- (Opsiyonel) Aylık özet JSON olarak bir LLM'e verilir, LLM sadece tavsiye cümlelerini yazar. Rakamlar her zaman servis hesabından gelir.

## 4. İleri vade tahmini (ForecastService)
- Tahmini ay sonu kalanı = bilinen gelir − olduğu gibi alınan giderler − (değişken giderler × ayın gün sayısı ÷ geçen gün sayısı)
  - Bilinen gelir = bu ay kayıtlı gelir + bekleyen tekrarlayan gelir.
  - Olduğu gibi alınan giderler = IsFixed kategorilerindeki giderler + tekrarlayan tanımdan oluşmuş giderler (kategorisi ne olursa olsun) + bekleyen tekrarlayan giderler.
  - Değişken gider = kalan tüm giderler. Yalnızca bunlar ay sonuna ölçeklenir.
  - "Bekleyen" = aktif bir tekrarlayan tanımın bu aya ait, henüz oluşmamış işlemi (bkz. 5). Geleceği belli olduğu için tahmine tutarıyla girer.
  - Örnek: ayın 7'si, gelir 4.000, sabit gider 2.200, değişken gider 250, ay 31 gün → 4.000 − 2.200 − 250 × 31 ÷ 7 ≈ 693 TL.
  - Örnek (bekleyenle): ayın 11'i, gelir 5.500, kira 3.000, değişken gider 1.100 → 5.500 − 3.000 − 1.100 × 31 ÷ 11 = −600 TL. Ayın 20'sinde gelecek 1.500 TL'lik tekrarlayan gelir tanımlıysa tahmin +900 TL olur.
  - Bu ay ne işlem ne de bekleyen tekrarlayan işlem varsa tahmin hesaplanmaz.
  - Ana sayfada, hedef veya geçmiş veri olmasa da gösterilir.
- Hedefe kalan süre (ay) = (HedefTutar − katkılar toplamı) ÷ ortalama aylık devir (yukarı yuvarla).
  - Aylık devir = o ayın geliri − o ayın tüm giderleri (işlemlerden hesaplanır, MonthClose kaydına bağlı değildir).
  - Ortalama = son 3 tamamlanmış ayın ortalaması; yalnızca işlemi olan aylar sayılır, negatif aylar dahildir.
  - Ortalama devir ≤ 0 ise: "Bu tempoyla hedefe ulaşmak için harcamaları azaltman gerek."
  - Aktif hedef yoksa veya hiç geçmiş ay verisi yoksa hesaplanmaz ve cümle gösterilmez.
- Ana sayfada tek cümle: "Bu tempoyla {Hedef} hedefine {Ay Yıl} civarında ulaşırsın."

## 6. Kategori bütçeleri (BudgetService, WarningService kural 5)
Kullanıcı bir gider kategorisine aylık limit koyar (ör. Kafe/Kahve 500 TL). Kategori başına en fazla bir limit vardır; gelir kategorilerine limit konamaz. Limit 0'dan büyük olmalı ve 2 ondalığa yuvarlanır.

**Kullanım düzeyi.** Kategorinin o ayki gider toplamı ÷ limit:
- ≤ %80 → sorun yok (yeşil çubuk)
- > %80 ve ≤ %100 → "yaklaşıyorsun" (altın çubuk)
- > %100 → "aştın" (kırmızı çubuk)

**Uyarı (kural 5).** Gider kaydedildiğinde o kategorinin düzeyi hesaplanır; düzey, o ay için daha önce bildirilen düzeyden yüksekse uyarı verilir ve hatırlanır (`WarnedPeriod`, `WarnedLevel`).
- Bir ayda en fazla iki uyarı çıkar: önce "yaklaşıyorsun", sonra "aştın"; her biri bir kez. Doğrudan limitin üstüne çıkılırsa yalnızca "aştın" gösterilir.
- Ay değişince hatırlanan düzey eskimiş olur; uyarılar yeniden mümkündür.
- Limit değiştirilirse hatırlanan düzey sıfırlanır (aynı değer yeniden kaydedilirse sıfırlanmaz).
- Harcama silinip toplam düşse de uyarı geri alınmaz ve aynı ayda aynı düzey yeniden bildirilmez.
- Yalnızca bu ay tarihli, kullanıcının kendi kaydettiği giderde çalışır; otomatik oluşan tekrarlayan işlemler uyarı üretmez ama toplama girer.
- Örnek: "Kafe/Kahve bütçenin %84'ünü kullandın: 420,00 TL / 500,00 TL. Ay bitmeden 80,00 TL kaldı." ve "Kafe/Kahve bütçeni aştın: 540,00 TL / 500,00 TL (40,00 TL fazla)."

**Gösterim.** Analiz sayfasında limiti olan her kategori için, seçili ayın harcamasıyla ilerleme çubuğu gösterilir (harcaması olmayanlar dahil; geçmiş aya bakılınca o ayın harcaması güncel limitle karşılaştırılır). Limitler `/Budgets` sayfasından eklenir, değiştirilir ve silinir.

## 7. CSV dışa aktarma (TransactionCsvExporter)
İşlemler sayfasındaki "CSV indir" düğmesi, **seçili ayın** işlemlerini Türkçe Excel'de doğrudan açılacak biçimde indirir (dosya adı `islemler-2026-10.csv`).

- **Biçim:** UTF-8 + BOM (Excel'in ü, ş, ğ, ı harflerini doğru okuması için); ayraç noktalı virgül (`;`); satır sonu CRLF; ondalık virgül.
- **Sütunlar:** `Tarih;Tür;Kategori;Ürün;Not;Tutar`
  - Tarih `gg.aa.yyyy`; Tür "Gelir" veya "Gider".
  - Tutar her zaman pozitiftir (yönü Tür sütunu belirler), iki ondalıkla ve binlik ayırıcı olmadan yazılır (`1234,50`); para simgesi yoktur, böylece Excel sayı olarak okur.
  - Biçim sunucunun kültüründen bağımsızdır (hep tr-TR).
- **Sıra:** Eskiden yeniye (ekrandaki liste ise yeniden eskiye). O ay işlem yoksa dosyada yalnızca başlık satırı olur.
- **Kaçışlar:** Ayraç, çift tırnak veya satır sonu içeren alan çift tırnak içine alınır, içindeki tırnaklar ikiye katlanır.
- **Formül koruması:** Alan `=`, `+`, `-`, `@` (veya sekme/satır başı) ile başlıyorsa başına `'` konur; yoksa Excel hücreyi formül gibi çalıştırır. Dosya başkasıyla paylaşılırsa bu bir güvenlik riskidir. Sonuç olarak "-5 indirim" gibi bir not dosyada `'-5 indirim` görünür.
- Yalnızca kullanıcının kendi işlemleri; geçersiz veya gelecek bir ay istenirse içinde bulunulan ay indirilir. Tekrarlayan tanımdan oluşan işlemler de dahildir.
- Not: Bölgesel ayarı Türkçe olmayan bir Excel `;` ayracını sütunlara bölmeyebilir; bu durumda "Veri → Metinden/CSV'den" ile içe aktarılır.

## 5. Tekrarlayan işlemler (RecurringTransactionService)
Kira, burs, aile desteği, abonelik gibi her ay aynı gün tekrarlanan gelir ve giderler bir kez tanımlanır; işlemleri uygulama oluşturur. Yalnızca aylık tekrar vardır.

**Oluşturma zamanı.** Arka plan işi yoktur. Giriş yapmış kullanıcının her sayfa isteğinde, sayfa çalışmadan önce günü gelmiş işlemler oluşturulur (`GenerateDueAsync`). Böylece ana sayfa, işlemler, analiz ve ay sonu devri her zaman güncel veriyi görür.

**Vade günü.** Bir ayın vadesi = min(tanımdaki gün, o ayın gün sayısı). 31 seçilirse Şubat'ta 28/29, Nisan'da 30 olur; tanımdaki gün değişmez.

**Hangi aylar oluşur.**
- Vadesi bugünden sonra olan işlem oluşmaz; oluşan işlem hiçbir zaman ileri tarihli değildir.
- Vadesi başlangıç tarihinden önce veya bitiş tarihinden sonra olan işlem oluşmaz.
- Kullanıcı uzun süre girmezse kaçan aylar kendi vade tarihleriyle sonradan oluşturulur (en fazla 12 ay geriye).
- Başlangıç tarihi, tanım oluşturulurken içinde bulunulan ayın 1'inden eski olamaz; yani yeni bir tanım geçmiş aylara işlem üretmez.

**Aynı ay iki kez oluşmaz.**
- Tanım, en son işlenen ayı (`LastGeneratedPeriod`) tutar; o ay ve öncesi bir daha ele alınmaz.
- Veritabanında (tanım, ay) üzerinde benzersiz index vardır; aynı anda gelen iki istek aynı ayı iki kez yazamaz.

**Düzenleme, durdurma, silme.**
- Tanımı düzenlemek yalnızca bundan sonra oluşacak işlemleri etkiler; oluşmuş işlemler değişmez.
- Gün değiştirilirse ayda yine en fazla bir işlem oluşur. O ayın işlemi henüz oluşmadıysa ve yeni gün geçmişte kaldıysa, işlem yeni güne tarihlenerek hemen oluşur.
- Durdurulan tanım işlem üretmez ve tahmine girmez. Devam ettirilince başlangıç tarihi bugüne çekilir: durduğu ayların işlemleri sonradan eklenmez. Bitiş tarihi geçmiş bir tanımın bitiş tarihi uzatılırsa da aynı kural geçerlidir.
- Tanım silinince oluşmuş işlemler kalır ve normal işleme dönüşür.
- Kullanıcı oluşmuş bir işlemi silerse o ay için yeniden oluşmaz; tanım sonraki aylarda çalışmaya devam eder.
- Kullanıcı oluşmuş bir işlemi düzenleyebilir (ör. o ay tutar farklıydı); tanım değişmez.

**"Her ay tekrarla".** Yeni işlem eklerken işaretlenirse, o işlem yeni bir tanımın ilk oluşumu sayılır; tanımın günü işlemin günüdür. İşlem geçmiş bir aya aitse aradaki aylar doldurulmaz, üretim içinde bulunulan aydan başlar.

**Hesaplara etkisi.**
- Tekrarlayan tanımdan oluşan giderler, kategorisi sabit olmasa da "sabit gider gibi" sayılır: tahminde ölçeklenmez (bkz. 4), kural 1'in ortalamasına girmez (bkz. 2), en çok harcanan ürünlerde görünmez (bkz. 3).
- Bu ay henüz oluşmamış işlemler ("bekleyenler") tahmine ve kural 4'e tutarlarıyla girer.
- Otomatik oluşan işlemlerde anında uyarılar çalışmaz; yerine "N tekrarlayan işlem eklendi: …" bildirimi gösterilir.
