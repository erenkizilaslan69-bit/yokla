# Tasarım rehberi — BudgetApp son dokunuş

Bu dosya, tasarım skill'lerinden (frontend-design, ui-ux-pro-max) bu uygulamaya uyan ilkelerin özetidir. Tasarım kararlarında bu dosya esas alınır. Mantığa, servislere ve testlere dokunulmaz; sadece görünüm, metin ve yerleşim değişir.

## Ürün ve kimlik

- **Ne:** Herkes için, sade ama karakterli kişisel bütçe uygulaması. Ses tonu samimi ve esprili ("Menüde makarna var" üslubun parçası, kalır).
- **Uygulamanın en karakteristik şeyi:** Günlük harcama hakkı. "Bugün ne kadar harcayabilirim?" sorusunun cevabı. Tasarımın yıldızı bu olmalı.
- **Korunacaklar:** Zümrüt yeşili + altın vurgu paleti, kategori renk ve ikon sistemi, koyu tema, ölçülmüş kontrast oranları.

## Ana ilke: cesaret tek bir yerde

Bir öğe akılda kalsın, çevresindeki her şey sakin ve disiplinli olsun.

- Ana sayfada **günlük harcama hakkı rakamı** tek büyük, iddialı tipografik öğe olsun. Başka hiçbir rakam onunla yarışmasın.
- Geri kalan her şey (gelir/gider özetleri, hedef, son işlemler) bir kademe sessizleşsin.
- Süsleme amaçlı gradyan, gölge ve renkli zeminler azaltılsın. Kalan her görsel öğe bir bilgi taşısın.

## Kaçınılacak "şablon" izleri

Bunlar yapay zekâ ile üretilmiş arayüzlerin tipik işaretleri; uygulamada varsa sadeleştir:

- **Her şeyi aynı karta bölmek:** Her bölüm aynı köşe yarıçapı, aynı yumuşak gri gölge ve aynı beyaz kutuda. Hiyerarşiye göre ayrıştır: en önemli öğe öne çıksın, ikincil bölümler kartsız (sadece başlık + ince ayraç) olabilir. Köşe yarıçapı ve gölge, öğenin önemine göre değişsin.
- **Dekoratif gradyanlar:** Hedef kartındaki büyük yeşil gradyan ve dekoratif daire, tek odak günlük hak olacaksa gereksiz rekabet yaratıyor. Düz renk veya çok hafif ton yeterli.
- **Bağlantı metnine "→" eklemek** ("Tümünü gör →"), her başlığın üstünde etiket, BÜYÜK HARF etiketler.
- **Her bölümde ayrı giriş animasyonu:** Hareket tek bir anda olsun (ör. sayfa açılınca günlük hak rakamının ya da hedef barının dolması). Kullanıcı eylemine cevap veren hareketler (açılma, onay) kalabilir.

## Tipografi

- Inter gövde metni için kalabilir. Ama **para rakamları için karakterli, rakamları güzel çizilmiş ikinci bir yazı tipi** düşün; uygulamanın kişiliği büyük rakamlarda görünür. Şartlar: Türkçe karakterler (ğ, ş, ı, İ) ve ₺ işaretini desteklemeli, yerel dosya olarak eklenmeli (CDN yok), lisansı açık olmalı. Uygun aday bulamazsan Inter'in kalın ağırlığıyla, sıkı harf aralığıyla devam et.
- Tüm tutarlarda **tabular (eş genişlikli) rakamlar** (`font-variant-numeric: tabular-nums`) ve sağa hizalama; listelerde rakamlar alt alta düzgün dursun.
- Net bir tip ölçeği: birkaç sabit boyut, belirli ağırlıklar. Gövde 16px, gövde metni 12px'in altına inmesin, satır yüksekliği ~1.5, satır uzunluğu 80 karakterin altında.

## Yerleşim

- 4/8 px boşluk ritmi; bölüm aralıkları hiyerarşiye göre (ör. 16 / 24 / 32 / 48).
- **Mobil gezinme:** Hamburger menü yerine altta en fazla 5 sekmeli bir gezinme çubuğu (Ana sayfa, İşlemler, Analiz, Hedefler + ortada ekle/fiş) değerlendir. Telefonda en çok kullanılan sayfalara tek dokunuşla ulaşılsın. Sabit çubuk içerik ve güvenli alanla (çentik, alt çizgi) çakışmasın; liste sonları çubuğun altında kalmasın.
- Masaüstünde içerik genişliği okunabilir sınırda kalsın.

## Etkileşim ve erişilebilirlik (vazgeçilmez taban)

- Dokunma hedefleri en az 44×44 px; özellikle mobil işlem listesindeki düzenle/sil ikonları. Gerekirse görünür ikon küçük kalıp dokunma alanı büyüsün.
- Sadece ikondan oluşan her düğmenin erişilebilir adı olsun (`aria-label`).
- Görünür klavye odağı, `prefers-reduced-motion` desteği, iki temada metin kontrastı ≥ 4.5:1, ikon/çubuk ≥ 3:1 (mevcut kontrast betiğiyle doğrula).
- Bilgi sadece renkle verilmesin (bütçe çubuklarında yüzde metni zaten var, korunacak).
- Basma geri bildirimi 80–150 ms; düzen kaydırmayan durum geçişleri.

## Metin

- Kullanıcının dilinde, etken çatı, düğme ne yapıyorsa onu söylesin ("Kaydet", "Fişi oku"); aynı eylem akış boyunca aynı adı taşısın.
- Hata mesajları ne olduğunu ve nasıl düzeltileceğini söylesin, özür dilemesin.
- Boş ekranlar bir eyleme davet etsin.
- Her metin tek bir iş yapsın; gereksiz açıklama satırlarını kes.

## Mevcut ekranlardan somut tespitler

**Ana sayfa**
- Günlük hak kartı en üstte ama rakamı Gelir/Gider rakamlarıyla aynı boyutta; asıl kahraman olarak belirgin şekilde büyümeli.
- Aynı bilgi iki kez yazıyor: "₺159,02 kaldı" ve hemen altında "Bugün ₺159,02 hakkın var, henüz harcama yapmadın". Tek cümleye indir.
- Harcama yokken boş ilerleme çubuğu bozuk bir öğe gibi duruyor; harcama yoksa gizle ya da farklı göster.
- **Çelişen iki mesaj:** Günlük hak "sonraki gelire kadar ₺2.385 kullanılabilir" derken hemen altında "Tahmini kalan −₺729,53" ve "ay sonunda açık verirsin" yazıyor. Biri tüm bakiyeye, diğeri sadece bu ayın gelir-giderine bakıyor; kullanıcı hangisine inanacağını bilemiyor. Ana sayfada tek bir hikâye anlatılmalı: kahraman günlük hak; tahmin ayrı bir kart değil, sadece risk varsa günlük hak kartının içinde tek satır uyarı. Gerekirse tahmin ifadesini "bu ayın gelirine göre" diye netleştir.
- Gelir tek başına tam genişlikte, Gider ve Tahmini kalan yan yana: üç ayrı ikonlu kart hem tutarsız hem şablon görünüyor. Tek kompakt satıra indir (ör. "Gelir ₺9.500 · Gider ₺6.964").
- Hedef kartının büyük yeşil gradyanı ve dekoratif dairesi günlük hakla yarışıyor; sakinleştir.
- Kamera ve "+" düğmeleri hâlâ içeriği örtüyor: hedef kartındaki "Tüm hedefler" bağlantısı ve footer'daki "Gizlilik". Alt gezinme çubuğuna geçilirse bu sorun kökten çözülür.
- Son işlemlerde aynı tarih her satırda tekrar ediyor; tarihe göre grupla ("Bugün", "10 Ekim").

**İşlemler**
- Özet kutuları ana sayfadakinin aynısı; burada da tek kompakt satır yeterli.
- "CSV indir" nadir kullanılan bir eylem ama başlıkta "Tekrarlayanlar" ile aynı ağırlıkta duruyor; ikincil bir menüye (⋯) taşınabilir.
- Satırlardaki düzenle/sil ikonları çok küçük dokunma hedefleri ve tutarın altına sıkışmış. Satırın tamamına dokununca düzenleme açılsın, silme düzenleme ekranında dursun.
- Tarihe göre gruplama burada da uygulansın.

**Analiz**
- "Geçen aya göre değişim" tablosu telefonda beş sütunla sıkışıyor; mobilde sadece kategori ve fark/% gösterilsin, ayrıntı genişleyince açılsın.
- Tavsiyeler her biri büyük ikon kutulu ayrı kartlarda, çok boşluk var; tek kart içinde sade bir liste yeterli.
- (Görünüm dışı not) "En çok harcanan ürünler"de "LATTE" ve "Latte" ayrı ürün sayılıyor. Bu bir mantık düzeltmesi: gruplama büyük/küçük harf duyarsız olmalı. Tasarım işinden ayrı, küçük bir commit olarak yapılabilir.

**Hedefler, giriş, menü**
- Giriş sayfası ve hedef kartı iyi durumda; büyük değişiklik gerekmiyor.
- Hamburger menüsü düzgün ama alt gezinme çubuğuna geçilirse tema seçimi ve çıkış bir "Hesap/Ayarlar" alanına taşınmalı.

## Süreç

1. Önce kısa bir tasarım planı: renk rolleri, tipografi, ana sayfa yerleşimi (ASCII taslakla), ilkeler.
2. Planı "bu, herhangi bir bütçe uygulaması için yapacağım varsayılan şey mi?" diye gözden geçir; varsayılan kalan kısımları değiştir ve neyi neden değiştirdiğini söyle.
3. Onaydan sonra uygula. Bitirince "aynadan bak, bir aksesuarı çıkar" kuralı: bir süsü kaldır.
