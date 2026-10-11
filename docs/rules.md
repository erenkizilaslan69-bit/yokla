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
2. **Gereksiz harcama payı:** IsUnnecessary kategorilerin bu ayki toplamı > bu ayki toplam gelirin %20'si.
3. **Kategori artışı:** Bir kategorinin bu ayın bugününe kadarki toplamı > geçen ayın aynı gününe kadarki toplamının %130'u. (Geçen ay o kategoride harcama yoksa çalışmaz.)
4. **Eksiye düşme riski:** Tahmini ay sonu kalanı (bkz. 4) < 0. Bu ay hiç gelir kaydı yoksa çalışmaz (gelir girilmeden her harcama "açık" görünürdü).

Kural 1 yalnızca gider, kural 2–4 kaydedilen giderin kategorisine göre çalışır (kural 2 sadece gereksiz kategorideki harcamada, kural 3 sadece o kategori için). Uyarılar yalnızca bu ay tarihli işlemler için hesaplanır.

Uyarı metinleri samimi ve kısa olsun, her zaman gerçek rakam içersin. Örnek:
"Bu ay dışarıda yemeğe 1.200 TL gitti, geçen ayın iki katı. Bu hızla ay sonunda menüde makarna var."

## 3. Aylık analiz (AnalysisService)
- Kategori bazında toplam gider (pasta grafik).
- ItemName bazında en çok harcanan 10 ürün (sabit gider kategorileri — IsFixed — hariç).
- Geçen aya göre kategori değişimi (TL ve %).
- 2–3 kural tabanlı tavsiye, örneğin:
  - En çok artan gereksiz kategori için: "{Kategori} harcamanı yarıya indirsen hedefine {N} ay erken ulaşırsın."
  - Sık tekrar eden ürün için: "{Ürün} için bu ay {Adet} kez toplam {Tutar} TL ödedin."
- (Opsiyonel) Aylık özet JSON olarak bir LLM'e verilir, LLM sadece tavsiye cümlelerini yazar. Rakamlar her zaman servis hesabından gelir.

## 4. İleri vade tahmini (ForecastService)
- Tahmini ay sonu kalanı = bu ayki gelir − bu ayki sabit giderler − (bu ayki değişken giderler × ayın gün sayısı ÷ geçen gün sayısı)
  - Sabit gider = IsFixed kategorilerindeki giderler; değişken gider = diğer tüm giderler. Sabit giderler olduğu gibi alınır, sadece değişken giderler ay sonuna ölçeklenir.
  - Örnek: ayın 7'si, gelir 4.000, sabit gider 2.200, değişken gider 250, ay 31 gün → 4.000 − 2.200 − 250 × 31 ÷ 7 ≈ 693 TL.
  - Bu ay hiç işlem yoksa tahmin hesaplanmaz.
  - Ana sayfada, hedef veya geçmiş veri olmasa da (bu ay en az bir işlem varsa) gösterilir.
- Hedefe kalan süre (ay) = (HedefTutar − katkılar toplamı) ÷ ortalama aylık devir (yukarı yuvarla).
  - Aylık devir = o ayın geliri − o ayın tüm giderleri (işlemlerden hesaplanır, MonthClose kaydına bağlı değildir).
  - Ortalama = son 3 tamamlanmış ayın ortalaması; yalnızca işlemi olan aylar sayılır, negatif aylar dahildir.
  - Ortalama devir ≤ 0 ise: "Bu tempoyla hedefe ulaşmak için harcamaları azaltman gerek."
  - Aktif hedef yoksa veya hiç geçmiş ay verisi yoksa hesaplanmaz ve cümle gösterilmez.
- Ana sayfada tek cümle: "Bu tempoyla {Hedef} hedefine {Ay Yıl} civarında ulaşırsın."
