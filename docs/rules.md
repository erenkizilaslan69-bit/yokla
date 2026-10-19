# İş kuralları

Kullanıcıya gösterilen bütün tutarlar tek biçimde yazılır: `₺1.234,50`, eksi tutarlar gerçek eksi işaretiyle (`−₺120,00`; `AppCulture.Money`). Saklanan tutarlar kuruşa yuvarlanır, yarımlar sıfırdan uzağa (`MoneyMath.Round`).

Hedef kitle: öğrenciler ve çalışanlar dahil herkes. Kurallar gelirin türüne (maaş, burs, serbest çalışma, aile desteği) bakmaz.

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

Metinler belirli bir kesime (öğrenci, çalışan) değil herkese hitap eder; "burs", "yurt" gibi yalnızca bir kesime özgü örnekler kullanılmaz. "Makarna" esprileri ("Menüde makarna var", "makarna günleri") bir kesime özgü değildir, uygulamanın üslubunun parçasıdır ve kalır.

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

## 5. Tekrarlayan işlemler (RecurringTransactionService)
Maaş, kira, fatura, abonelik, burs gibi her ay aynı gün tekrarlanan gelir ve giderler bir kez tanımlanır; işlemleri uygulama oluşturur. Yalnızca aylık tekrar vardır.

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
- Örnek: "Kafe/Kahve bütçenin %84'ünü kullandın: ₺420,00 / ₺500,00. Ay bitmeden ₺80,00 kaldı." ve "Kafe/Kahve bütçeni aştın: ₺540,00 / ₺500,00 (₺40,00 fazla)."

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

## 8. Günlük harcama hakkı (DailyAllowanceService)
Ana sayfanın en üstündeki kart "bugün ne kadar harcayabilirim" sorusunu cevaplar: eldeki para, bir sonraki gelire kadar gün gün bölünür.

**Sonraki gelir.** Aktif tekrarlayan gelir tanımlarının bugünden sonraki en yakın oluşumu (başlangıç/bitiş tarihleri ve "31 → ayın son günü" kuralı geçerlidir; durdurulmuş tanımlar sayılmaz). Gelir bugün geldiyse sonraki, bir sonraki oluşumdur.
- Kalan gün = sonraki gelirin tarihi − bugün (bugün dahil, gelir günü hariç).
- Hiç tekrarlayan gelir yoksa dönem ayın son gününe kadar sürer (kalan gün = ay sonu − bugün + 1) ve kartta "Gelirini tekrarlayan işlem olarak eklersen daha doğru hesaplarım" ipucu çıkar.

**Kullanılabilir para (gün başına göre).**
- Eldeki para = bugüne kadarki tüm gelirler (bugünküler dahil) − dünden önceki tüm giderler − bugünkü "sabit gibi" giderler − hedeflere giden tüm para (elle ekleme ve ay sonu devri).
  - Bakiye **tüm zamanlar** üzerinden hesaplanır, ay başında sıfırlanmaz: geçen aydan kalan para da geçen ayın açığı da sonraki aya taşınır. Ay sonu devri yapılınca aktarılan tutar havuzdan düşer.
  - "Sabit gibi" gider = sabit kategorideki veya tekrarlayan tanımdan oluşan gider (bkz. 5). Bugün çekilen kira "bugünün harcaması" sayılmaz, havuzdan düşer.
- Bekleyen giderler = henüz oluşmamış tekrarlayan gider oluşumlarından vadesi sonraki gelirden **önce** olanlar. Sonraki gelir gelecek aydaysa aradakiler de dahildir; gelirle aynı gün olanlar o gelirden ödeneceği için sayılmaz.
- Kullanılabilir = eldeki para − bekleyen giderler.

**Günlük hak.**
- Günlük hak = kullanılabilir ÷ kalan gün (kuruşa aşağı yuvarlanır; kullanılabilir ≤ 0 ise 0).
- Bugün harcanan = bugünkü değişken giderler. Bugün kalan = günlük hak − bugün harcanan.
- Hak gün başına göre hesaplandığı için gün içinde harcadıkça küçülmez. Az harcanan günün artanı da, aşılan günün fazlası da ertesi günün hakkına kendiliğinden yansır: yarınki hak = (kullanılabilir − bugün harcanan) ÷ (kalan gün − 1). Ayrı bir kayıt tutulmaz; her şey işlemlerden hesaplanır.
- Örnek: eldeki 800 TL, maaş 8 gün sonra → günlük hak 100 TL. Bugün 30 TL harcanırsa "Bugün 100 TL hakkın var, 30 TL harcadın, 70 TL kaldı"; yarınki hak 770 ÷ 7 = 110 TL olur.

**Kartın durumları.**
- Normal: bugün kalan ≥ 0.
- Bugünü aştı (altın): bugün harcanan günlük hakkı geçti ama sonraki gelire kadar hâlâ para var; yarınki hak gösterilir.
- Açık (kırmızı): sonraki gelire kadar para kalmadı. Ya gün başında kullanılabilir ≤ 0'dı ya da bugünkü harcama kullanılabilir paranın tamamını bitirdi; ikinci durumda kart ertesi günü beklemeden hemen kırmızıya döner.
  - Şu an kalan = kullanılabilir − bugün harcanan. Açık = bu değer negatifse mutlak değeri; "Sonraki gelire kadar {açık} açığın var" uyarısı çıkar.
  - Şu an kalan tam 0 ise ve önde başka gün varsa yine açık sayılır ("harcayacak paran kalmadı"). Gelir yarınsa (kalan gün 1) son hakkı tam harcamak açık değildir, normaldir.
- Kullanıcının hiç işlemi yoksa kart gösterilmez.

**Tempo satırı (SpendingPace).** Ay sonu tahmini ana sayfada ayrı bir kart olarak gösterilmez; günlük hakla çelişiyor gibi görünüyordu, çünkü başka bir şeyi (bu ayın gelir − gider sonucunu) başka bir birimle ölçüyordu. Onun yerine, aynı parayı ve aynı birimi (gün) kullanan bir satır günlük hak alanının içinde çıkar:
- Tempo = bu ayki değişken giderler ÷ ayın geçen günleri (bugün dahil), kuruşa yuvarlanır (`BalanceProjection.DailyPace`). Değişken gider, günlük hakkın saydığı harcamayla aynıdır: sabit kategoriler ve tekrarlayan tanımdan oluşanlar hariç.
- Paranın biteceği gün = bugün + ⌊kullanılabilir ÷ tempo⌋ gün. Kullanılabilir, günlük hakkın bölüştürdüğü paradır (gün başına göre).
- Satır yalnızca bu gün **sonraki gelirden (dönem sonundan) önceyse** görünür: "Bu ay günde ortalama ₺233,21 harcıyorsun, hakkın ₺159,02. Bu tempoyla elindeki para 27 Ekim'de biter." Bugün ya da yarınsa "bugün biter" / "yarın biter" yazar.
- Tempo günlük haktan **en az %5 fazla** olmalı (`SpendingPace.MinOverspend`). Daha küçük bir fark, kuruş yuvarlaması yüzünden parayı bir gün erken bitirse bile uyarı değil gürültüdür.
- Görünmediği durumlar: ayın ilk 3 günü (uyarı kuralı 1 gibi; tek bir büyük harcama tempoyu belirlemesin), bu ay hiç değişken harcama yoksa, kullanılabilir para yoksa (kart zaten "açık" durumundadır).
- Ay sonu tahmini (bölüm 4) hesaplanmaya devam eder; uyarı kuralı 4 onu kullanır.

**Bilinen sınırlar.**
- Bakiye geçmiş kayıtlara dayanır: uygulamaya yeni başlayıp cebindeki parayı girmemiş kullanıcıda düşük, biriken parasını hedefe aktarmayan kullanıcıda yüksek çıkar.
- "Sonraki gelir" tutarına bakılmaksızın en yakın tekrarlayan gelirdir; küçük bir gelir dönemi kısaltır.
- Tutarı değişen tanımlar (elektrik, part-time) tanımdaki tahmini tutarla hesaba girer.
- Geçmiş tarihli bir gider girilirse havuz küçülür ve bugünkü hak o an değişir.

## 9. Fiş okuma (ReceiptService, IReceiptParser)
Kullanıcı fişin fotoğrafını çeker; okunan satırları onaylar, her onaylanan satır ayrı bir gider olarak kaydedilir.

**Okuyucu.** `IReceiptParser` sağlayıcıyı gizler.
- Anahtar varsa (`Gemini:ApiKey`, user-secrets veya `Gemini__ApiKey` ortam değişkeni) `GeminiReceiptParser`: Gemini `generateContent` uç noktası, model `Gemini:Model` (varsayılan `gemini-3.8-flash`), yanıt bir JSON şemasıyla sınırlanır. Anahtar URL'de değil `x-goog-api-key` başlığında gider.
- **Düşünme seviyesi** `Gemini:ThinkingLevel` → `generationConfig.thinkingConfig.thinkingLevel`. Varsayılan `low`: fiş okumak az düşünme ister, uzun düşünme ise cevabı geciktirir. `gemini-3.8-flash` `low`, `medium`, `high` kabul eder (`minimal` bu modelde hata verir, başka modellerde geçerlidir); boş bırakılırsa alan gönderilmez ve model kendi varsayılanını kullanır (3.8 Flash için `medium`).
- `temperature`, `topP`, `topK` gönderilmez: Gemini 3.6 ve sonrası bunları yok sayar, sonraki modeller reddedecek.
- Anahtar yoksa Development'ta `DemoReceiptParser` (birkaç hazır, uydurma fiş; aynı fotoğrafa hep aynı sonuç; ekranda "demo modu" yazar). Başka ortamlarda özellik kapalıdır: sayfalar 404 döner, giriş noktaları görünmez.
- Modelden istenenler: işletme adı, tarih (yyyy-MM-dd), fiş toplamı, genel indirim ve her satır için ham metin, anlaşılır ürün adı ("SUT YRM YGL" → "Yarım yağlı süt"), miktar, birim, satır tutarı ve kategori. Kategori, kullanıcının **gider kategorileri** arasından seçilmek zorundadır (şemada enum).
- İstem Türk fişlerine göredir: TOPLAM, KDV, ödeme, kasiyer, VKN/TCKN gibi satırlar ürün değildir; "2 X 12,50" ve "0,755 KG X 89,90" satırları miktar ve satır toplamı olarak okunur; satıra ait indirim o satırdan düşülür; sayılar Türk biçimindedir; kart numarası, ad, TCKN gibi bilgiler hiçbir alana yazılmaz. Miktar ürün adına eklenmez.

**Fotoğraf.**
- Tarayıcıda uzun kenarı en fazla `Receipts:MaxImageEdge` (2048) piksele küçültülür ve JPEG olarak gönderilir. Tarayıcı fotoğrafı açamazsa (ör. Safari dışında HEIC) olduğu gibi gönderilir.
- Sunucu türü dosyanın ilk baytlarından anlar: yalnızca JPEG, PNG, WebP, HEIC/HEIF; en fazla `Receipts:MaxUploadBytes` (4 MB).
- Fotoğraf yalnızca bellekte işlenir; diske ya da veritabanına yazılmaz.

**Günlük sınırlar.** Gemini'ye giden her deneme `ReceiptScan` olarak kaydedilir (bkz. model.md). Kayıt servise gitmeden önce yazılır (Pending), böylece art arda iki gönderim de sayılır.
- Kullanıcı başına günde `Receipts:DailyLimitPerUser` (10). Sayılanlar: başarılı denemeler, model cevap verdiği hâlde kullanılabilir fiş çıkmayanlar (fiş değil, anlaşılamayan cevap, güvenlik engeli) ve cevabı beklenenler. **Cevap gelmeyen denemeler sayılmaz:** zaman aşımı, ağ ya da sunucu hatası, Google'ın hız veya kota sınırı, yapılandırma hatası. Bunlar kullanıcının elinde değildir; sayılsalardı servis yavaşken kullanıcı hiç fiş okutamadan hakkını bitirebilirdi.
- Uygulama geneli günde `Receipts:DailyLimitTotal` (300), **fiş okuma ve söyle ya da yaz için ortak**: ücretsiz kota tek anahtara aittir. Buna **her deneme sayılır**, cevap gelmeyenler de; bizim tarafta zaman aşımına uğrayan bir istek Google'da işlenmeye devam edip kotadan düşmüş olabilir.
- Gün, sunucunun yerel günüdür. Demo denemeleri sınırlara sayılmaz.

**Log.** Her Gemini isteği için model, düşünme seviyesi, görselin boyutu, HTTP durumu, geçen süre (ms), bitiş nedeni ve token sayıları (istem, cevap, düşünme) loga yazılır; zaman aşımında beklenen süre, hata durumunda Google'ın hata metni (ilk 500 karakter) yazılır. API anahtarı hiçbir log satırına girmez: yalnızca istek başlığında taşınır ve HttpClient loglarında başlık değerleri gizlenir. Fotoğraf ve okunan satırlar loglanmaz.

**Onay ekranı.** Ekran yükleme isteğinin cevabıdır; okunan sonuç da kaydedilene kadar hiçbir yerde saklanmaz (sayfa yenilenirse fotoğraf yeniden gönderilir ve bir hak daha kullanılır).
- Tarih: okunamadıysa bugün konur; gelecekteyse bugün konur ve kullanıcı uyarılır; bir yıldan eskiyse kontrol etmesi istenir. Kayıtta gelecek tarih her durumda reddedilir.
- Kategori: model listede olmayan ya da boş bir kategori dönerse "Diğer Gider".
- Genel indirim (hiçbir satıra bağlanamayan, fişin geneline uygulanan indirim) satırlara tutarlarıyla orantılı dağıtılır; kuruşa aşağı yuvarlanır, kalan kuruşlar en büyük satırdan düşülür. Ekranda "… genel indirim, tutarlarına oranla satırlara dağıtıldı" notu çıkar. İndirim fiş tutarı kadar ya da daha büyükse dağıtılmaz, kullanıcı uyarılır.
- 0,00 TL'lik satırlar (ör. ücretsiz poşet) gösterilmez. Hiç tutarlı satır kalmazsa ya da görsel fiş değilse "okunabilir bir fiş bulamadım" hatası verilir.
- **Toplam kontrolü:** seçili satırların toplamı fiş toplamından 0,05 TL'den fazla farklıysa uyarı çıkar; kullanıcı satırları düzenledikçe ya da çıkardıkça canlı güncellenir.
- **Aynı fiş iki kez:** aynı gün, aynı işletme notuyla ("Fiş · {işletme}") kayıtlı işlemler varsa uyarı çıkar. Toplamları aynıysa "zaten kaydedilmiş görünüyor"; farklıysa (ör. geçen sefer bir satır çıkarılmış ya da aynı gün ikinci alışveriş) "aynı fişse kontrol et". Elle girilen giderler bu kontrole girmez.
- Kullanıcı ürün adını, tutarı ve kategoriyi değiştirebilir, satırları çıkarabilir, tarihi ve işletme adını düzeltebilir.

**Kayıt.** Seçili her satır ayrı bir `Transaction` olur: Type = Gider, ItemName = ürün adı (en fazla 200 karakter), Note = "Fiş · {işletme}". Satırlar hep birlikte kaydedilir ya da hiçbiri kaydedilmez (`TransactionService.CreateManyAsync`); tek giderdeki kurallar her satıra uygulanır (tutar > 0, gelecek tarih yok, gider kategorisi). Ardından anında uyarılar her satır için çalışır, aynı uyarı bir kez gösterilir.

**Hata mesajları.**

| Durum | Mesaj |
| --- | --- |
| Zaman aşımı (`Gemini:TimeoutSeconds`, 60 sn) / 408 / 504 | Fiş okunması uzun sürdü. Biraz sonra tekrar dene. |
| 429, günlük kota | Fiş okuma servisinin bugünkü kotası doldu. Yarın tekrar deneyebilirsin. |
| 429, kısa süreli sınır | Kısa sürede çok fazla fiş gönderildi. Bir dakika sonra tekrar dene. |
| Kullanıcının günlük sınırı | Bugünkü fiş hakkını kullandın (10/10). Yarın tekrar dene ya da işlemleri elle ekle. |
| Uygulama geneli sınır | Fiş okuma bugün herkes için yoğundu ve günlük sınıra ulaştı. … |
| Fiş değil / satır yok | Bu görselde okunabilir bir fiş bulamadım. Fişi düz bir zeminde, yakından ve iyi ışıkta çek. |
| Geçersiz JSON, şemaya uymayan ya da kesilmiş cevap | Fiş okunurken bir sorun oldu, sonuç anlaşılamadı. Tekrar dene. |
| Güvenlik engeli | Bu görsel işlenemedi. Başka bir fotoğraf dene. |
| 400 / 401 / 403 / 404 (anahtar, model, istek) | Fiş okuma şu an kullanılamıyor. (ayrıntı yalnızca loga yazılır) |
| 503, model aşırı yoğun ("high demand"; yeniden denemelerden sonra da) | Google'ın fiş okuma servisi şu an çok yoğun. Birkaç dakika sonra tekrar dene. |
| Diğer 5xx, ağ hatası | Fiş okuma servisine şu an ulaşılamıyor. Biraz sonra tekrar dene. |
| Dosya türü / boyutu | Bu dosya kabul edilmedi: JPEG, PNG, WebP veya HEIC bir fotoğraf, en fazla 4 MB. |

**Yeniden deneme yalnızca 503'te.** Model aşırı yoğun olduğunda (HTTP 503) istek kısa ve giderek uzayan aralarla yeniden gönderilir: önce 1 sn, sonra 2 sn bekleyip (`Gemini:RetryDelaysSeconds`, varsayılan `[1, 2]`), en fazla üç deneme. Denemeler ve aralar birlikte tek zaman aşımının (`Gemini:TimeoutSeconds`, 60 sn) içinde kalır: bir sonraki bekleme süreyi aşacaksa başlatılmaz ve kullanıcıya "çok yoğun" mesajı gösterilir. Her deneme loga yazılır. 503 cevabı token harcamaz; yine de yoğunluk sürerse deneme kullanıcının hakkından düşmez (cevap gelmedi sayılır). Diğer hatalarda yeniden deneme yoktur: 429'da kota dolmuştur ve tekrar denemek kotayı daha da harcar; 500 ve ağ hataları da hemen bildirilir. Söyle ya da yaz aynı istemciyi kullanır, kural ikisinde aynıdır.

**Yedek model.** Asıl model yeniden denemelerden sonra hâlâ 503 veriyorsa (ya da bir sonraki bekleme süreyi aşacaksa) istek **bir kez** yedek modele gider: `Gemini:FallbackModel`, varsayılan `gemini-3.5-flash-lite` (Gemini dokümanına göre görsel, ses ve PDF girdisi, yapılandırılmış çıktı ve `low` düşünme seviyesi destekli). Yedek model de aynı zaman aşımının içinde kalır ve tekrar denenmez; onun hatası olduğu gibi bildirilir (503 ise "çok yoğun" mesajı). Yalnızca 503 yedeğe geçirir; 429, 500, 400 gibi hatalar geçirmez. Yedeğin kullanıldığı ve adı loga yazılır ("... still overloaded after 3 tries; using the fallback model gemini-3.5-flash-lite once"). Boş ya da asıl modelle aynı ad yedeği kapatır. Fiş okuma ve Söyle ya da yaz için aynıdır.

**Gizlilik.** Yükleme sayfası, fotoğrafın okunması için Google'ın Gemini servisine gönderildiğini, sunucuda saklanmadığını ve ücretsiz kullanımda Google'ın içeriği ürünlerini geliştirmek için kullanabileceğini, insanların inceleyebileceğini söyler; fişteki kart numarası, ad soyad gibi bilgilerin kapatılmasını önerir. Gemini API şartları 18 yaş sınırı ve AB/İngiltere/İsviçre kullanıcıları için ücretli katman şartı da getirir (bkz. README).

**Bilinen sınırlar.**
- Model aynı ürünü farklı fişlerde farklı adlandırabilir; bu, Analiz'deki "en çok harcanan ürünler" gruplamasını bölebilir.
- İade, iptal ve e-fatura belgeleri kapsam dışıdır.
- Yapılandırılmış çıktı `generationConfig.responseMimeType` + `responseJsonSchema` ile istenir. Güncel `generateContent` dokümanındaki REST örneği bunun yerine `generationConfig.responseFormat.text.{mimeType, schema}` gösteriyor; eski alanlar için bir kullanımdan kaldırma notu yok ve ilk gerçek istek 400 dönmedi. Testler sahte yanıtlarla çalışır.

## 10. Söyle ya da yaz (QuickEntryService, IQuickEntryParser)
Kullanıcı "simitçiye 20, otobüse 15, markete 230" gibi bir cümleyi söyler ya da yazar; cümle ayrı gider/gelir satırlarına bölünür. Açık olan satırlar hemen kaydedilir, şüpheli olan her şey önce onay ekranında gösterilir. Giriş noktası: telefonda "+" menüsü, masaüstünde İşlemler sayfasındaki "Söyle ya da yaz" düğmesi.

**Ses.** Tarayıcıda yazıya çevirme yoktur: ses `MediaRecorder` ile kaydedilir ve Gemini'ye **ses olarak** gönderilir (çok modlu girdi).
- Kayıt en fazla `QuickEntry:MaxRecordingSeconds` (30) sn sürer; son 5 saniyede geri sayım görünür, sürenin sonunda kendiliğinden durur. 0,5 sn'den kısa kayıt gönderilmez. Escape ya da sayfadan çıkmak kaydı atar ve mikrofonu bırakır.
- Boyut sınırı `QuickEntry:MaxAudioBytes` (2 MB; 30 sn tarayıcı kaydı 1 MB'ın çok altında), istek sınırı 3 MB.
- Sunucu türü tarayıcının söylediğine değil dosyanın ilk baytlarına bakarak belirler: WebM (Chrome, Edge, Firefox) → `audio/webm`, Ogg → `audio/ogg`, WAV → `audio/wav`, MP4 kabı (Safari'nin `audio/mp4` kaydı, iPhone Sesli Notlar `.m4a`) → `audio/m4a`. Gemini'nin desteklediği türler arasında `audio/mp4` yoktur, `audio/m4a` ve `audio/aac` vardır; MP4 kabı bu yüzden m4a olarak gönderilir. MP4 ailesinden resimler (HEIC, AVIF) reddedilir.
- Ses yalnızca bellekte işlenir; diske, veritabanına ya da loga yazılmaz. Loga yalnızca boyutu ve türü girer.
- Mikrofon izni reddedilirse: "Mikrofon izni verilmedi. Tarayıcının site ayarlarından izin verebilir ya da aşağıya yazabilirsin." ve imleç yazı kutusuna geçer. Mikrofon yoksa "Mikrofon bulunamadı." Tarayıcı kayıt yapamıyorsa (HTTPS değil, `MediaRecorder` yok) mikrofon düğmesi hiç gösterilmez.

**Yazı.** Aynı sayfadaki kutuya yazılan metin aynı ayrıştırmadan geçer (en fazla `QuickEntry:MaxTextLength`, 500 karakter). Metin istemde işaretler arasında **veri** olarak gider; içindeki talimatlara uyulmaması istenir. JavaScript olmadan da çalışır.

**Ayrıştırma.** `GeminiQuickEntryParser`, fiş okumayla aynı `GeminiClient` üzerinden (anahtar başlıkta, JSON şeması, düşünme seviyesi, zaman aşımı, log). İstemde bugünün tarihi ve haftanın günü, gider ve gelir kategorilerinin adları ile Türkçe sayı örnekleri ("yirmi beş", "iki buçuk", "bin iki yüz", "kırk lira elli kuruş") vardır. Yanıt şeması:
- `heard`: duyulan cümle (kullanıcıya gösterilir; yazılı girdide kullanıcının kendi metni gösterilir),
- `isEntry`: konuşmada hiç para geçmiyorsa false,
- her satır için `description`, `amount` (duyulmadıysa null), `type` (expense/income), `category` (gider ve gelir kategorileri, enum), `date` (yyyy-MM-dd; "dün", "cuma", "ayın 3'ü"; söylenmediyse null = bugün), `confidence` (high/medium/low) ve `issue` (modelin emin olamadığı şey, Türkçe).

**Satırların denetimi (QuickEntryRules.Resolve).** Modelin dediğine körü körüne güvenilmez:
- Modelin `issue` cümlesi aynen gösterilir.
- Tutar kuruşa yuvarlanır; yoksa ya da 0'dan küçükse boş bırakılır (model bir neden yazmadıysa: "Tutarı duyamadım, yazar mısın?").
- Kategori satırın türünde ve listede değilse "Diğer Gider" / "Diğer Gelir" konur: "Kategoriyi seç."
- İleri tarih bugüne çekilir ve söylenir; 60 günden eski tarih korunur ama sorulur.
- Yüksek güvende olmayan, başka sorunu olmayan satıra "Emin olamadım, kontrol et." yazılır.

**Hemen kaydet mi, onaya mı? (QuickEntryRules.CanAutoSave).** Satırlar ancak şunların **hepsi** doğruysa sorulmadan kaydedilir: bir şey duyulmuş, 1–8 satır, her satırın güveni `high`, tutarı var, kategoriyi model kendisi seçmiş ve türüyle uyumlu, modelin şüphesi yok, tarih son 7 gün içinde. Biri bile tutmazsa bütün satırlar onay ekranına gider. Modelin güven bilgisi bir ölçüm değildir; asıl güvence kuralların sıkılığı ve "Geri al"dır.

**Hemen kaydedilince.** İşlemler sayfasına (son satırın ayına) dönülür ve bir bildirim çıkar: "3 işlem eklendi: Simit ₺20,00 · Otobüs ₺15,00 · Market alışverişi ₺230,00", altında "Duyduğum: …", **Geri al** ve **Düzenle**.
- **Geri al:** bu işlemleri tek seferde siler. Yalnızca kullanıcının, notu "Hızlı giriş" olan işlemlerine dokunur; başka bir işlemin id'si gelse bile silinmez. Zaten silinmişse "Geri alınacak işlem bulunamadı" der.
- **Düzenle:** onay ekranını kaydedilmiş satırlarla açar. Kaydedilirse eski satırlar yenileriyle **tek bir veritabanı işleminde** değiştirilir (`TransactionService.ReplaceManyAsync`); yeni satırlardan biri geçersizse hiçbir şey değişmez. Vazgeçilirse kaydedilmiş satırlar olduğu gibi kalır.
- Bildirim tek seferliktir; sonradan değiştirmek için İşlemler sayfası kullanılır.

**Onay ekranı.** Fiş onay ekranının satır düzenini, satır çıkarma/geri alma denetimini (`_ReviewLineToggle`) ve betiğini (`receipt-review.js`) paylaşır. Her satırda açıklama, tutar, kategori (Gider ve Gelir gruplu tek liste; kategori türü de belirler) ve tarih vardır; sorunlu satırın altında nedeni yazar. Toplam gösterilmez (gelir ve gider karışabilir), kaydedilecek satır sayısı gösterilir.

**Kayıt.** Fişle aynı yol: `TransactionService.CreateManyAsync`, hepsi ya da hiçbiri; tutar > 0, gelecek tarih yok, kategori türü uyumlu. ItemName = açıklama (en fazla 200 karakter), Note = "Hızlı giriş". Ardından **anında uyarılar** her satır için çalışır, aynı uyarı bir kez gösterilir.

**Günlük sınırlar.** Denemeler `ReceiptScan` olarak, `Kind = QuickEntry` ile kaydedilir.
- Kullanıcı başına günde `QuickEntry:DailyLimitPerUser` (30), fiş hakkından **ayrı**: hızlı giriş günde birçok kez kullanılan küçük bir istek.
- Uygulama geneli sınır fiş okumayla **ortak** (`Receipts:DailyLimitTotal`, 300).
- Yazıyla giriş de sayılır (o da Gemini'ye gider). Konuşma içermeyen ya da para geçmeyen bir kayıt sayılır (model cevap verdi). Cevap gelmeyen denemeler (zaman aşımı, 5xx, hız/kota sınırı, yapılandırma) fişteki gibi kullanıcının hakkından düşmez. Demo denemeleri sayılmaz.

**Demo modu.** Anahtar yoksa Development'ta `DemoQuickEntryParser`: yazılan metni basit kurallarla gerçekten böler (virgül, noktalı virgül, "ve"; "simit", "otobüs", "market", "burs" gibi anahtar kelimeler; Türk biçimli sayılar; "dün" bütün satırlara uygulanır). Sayıların yazıyla söylenişini anlamaz. Ses kaydını dinlemez; her zaman "Simitçiye 20, otobüse 15, markete 230." döner.

**Geliştirme için deneme komutu.** iPhone sesini uygulamada denemek HTTPS ister; bunun yerine Development'ta:
```
dotnet run --project src/BudgetApp -- quick-entry-test kayit.m4a
dotnet run --project src/BudgetApp -- quick-entry-test kayit.m4a --mime audio/mp4
```
Komut dosyayı uygulamanın kullandığı gerçek ayrıştırıcıyla Gemini'ye gönderir; algılanan türü, süreyi, duyulan cümleyi, satırları ve uygulamanın ne yapacağını (hemen kaydet / onay ekranı) yazdırır. Hiçbir şey kaydedilmez, günlük sınıra sayılmaz. `--mime` başka bir türle göndermeyi dener (ör. m4a reddedilirse `audio/mp4` ya da `audio/aac`).

**Hata mesajları.**

| Durum | Mesaj |
| --- | --- |
| Boş girdi | Önce harcamanı söyle ya da yaz. |
| Ses dosyası değil / çok büyük | Ses kaydı alınamadı. Tekrar kaydet ya da yazarak gir. |
| Para geçmiyor | Söylediğinde bir harcama ya da gelir bulamadım. "Simitçiye 20, otobüse 15" gibi tutarlarla tekrar dener misin? |
| Kullanıcının günlük sınırı | Bugünkü söyle ya da yaz hakkını kullandın (30/30). … |
| Model aşırı yoğun (503, yeniden denemelerden sonra) | Google'ın servisi şu an çok yoğun. Birkaç dakika sonra tekrar dene ya da işlemleri elle ekle. |
| Zaman aşımı, kota, ağ, yapılandırma | Fiş okumadaki karşılıklarıyla aynı anlamda, "Söyle ya da yaz" diliyle |

**Gizlilik.** Sayfa ve Gizlilik sayfası, ses kaydının ya da metnin işlenmek üzere Google'ın Gemini servisine gönderildiğini, Yokla'nın sesi saklamadığını, ücretsiz katmanda Google'ın içeriği ürünlerini geliştirmek için kullanabileceğini söyler; kayıtta kart numarası gibi bilgilerin söylenmemesini ve yanındakilerin sesinin de kayda girebileceğini hatırlatır.

**Bilinen sınırlar.**
- iPhone Safari'nin MediaRecorder kaydı (parçalı MP4 olabilir) gerçek API ile denenmedi; Sesli Notlar `.m4a` dosyası deneme komutuyla denenebilir. m4a reddedilirse yedek yol: tarayıcıda Web Audio ile çözüp 16 kHz mono WAV'a çevirmek.
- Mikrofon yalnızca HTTPS'te ya da localhost'ta açılır.
- "Geri al", bildirim gösterildikten sonra kullanıcının o satırlarda yaptığı değişiklikleri de siler.
- "yüz elli" / "yüz, elli", "iki buçuk" (2,5 mi 250 mi) gibi belirsizlikler modele bırakılır; şüphe varsa onay ekranı açılır.
