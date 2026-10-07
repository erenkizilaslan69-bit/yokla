# İş kuralları

## 1. Ay sonu devri (MonthCloseService)
- Kullanıcı yeni ayda uygulamayı açtığında, geçen ay için MonthClose kaydı yoksa ana sayfada bir kart gösterilir.
- Kalan = geçen ayın gelirleri − geçen ayın giderleri.
- Kalan > 0 ve aktif bir hedef varsa: "Geçen ay {Kalan} TL artırdın. {Hedef} hedefine aktaralım mı?"
  - Onay: GoalContribution (Source = MonthClose) oluşur, MonthClose.TransferredGoalId dolar.
  - Ret: MonthClose kaydedilir, aktarım yapılmaz.
- Kalan ≤ 0: MonthClose kaydedilir, aktarım yapılmaz, kartta "Geçen ay {|Kalan|} TL açık verdin" yazar.
- Aynı ay iki kez kapatılamaz.

## 2. Anında uyarılar (WarningService)
Gider kaydedildikten hemen sonra çalışır, tetiklenen uyarılar sayfanın üstünde gösterilir.

1. **Büyük harcama:** Tek harcama > bu ayki günlük ortalama giderin 3 katı. (Ayın ilk 3 gününde bu kural çalışmaz.)
2. **Gereksiz harcama payı:** IsUnnecessary kategorilerin bu ayki toplamı > bu ayki toplam gelirin %20'si.
3. **Kategori artışı:** Bir kategorinin bu ayın bugününe kadarki toplamı > geçen ayın aynı gününe kadarki toplamının %130'u. (Geçen ay o kategoride harcama yoksa çalışmaz.)
4. **Eksiye düşme riski:** Tahmini ay sonu kalanı (bkz. 4) < 0.

Uyarı metinleri samimi ve kısa olsun, her zaman gerçek rakam içersin. Örnek:
"Bu ay dışarıda yemeğe 1.200 TL gitti, geçen ayın iki katı. Bu hızla ay sonunda menüde makarna var."

## 3. Aylık analiz (AnalysisService)
- Kategori bazında toplam gider (pasta grafik).
- ItemName bazında en çok harcanan 10 ürün.
- Geçen aya göre kategori değişimi (TL ve %).
- 2–3 kural tabanlı tavsiye, örneğin:
  - En çok artan gereksiz kategori için: "{Kategori} harcamanı yarıya indirsen hedefine {N} ay erken ulaşırsın."
  - Sık tekrar eden ürün için: "{Ürün} için bu ay {Adet} kez toplam {Tutar} TL ödedin."
- (Opsiyonel) Aylık özet JSON olarak bir LLM'e verilir, LLM sadece tavsiye cümlelerini yazar. Rakamlar her zaman servis hesabından gelir.

## 4. İleri vade tahmini (ForecastService)
- Tahmini ay sonu kalanı = bu ayki gelir − (bugüne kadarki gider × ayın gün sayısı ÷ geçen gün sayısı)
- Hedefe kalan süre (ay) = (HedefTutar − katkılar toplamı) ÷ son 3 ayın ortalama pozitif devri (yukarı yuvarla).
  - Ortalama devir ≤ 0 ise: "Bu tempoyla hedefe ulaşmak için harcamaları azaltman gerek."
- Ana sayfada tek cümle: "Bu tempoyla {Hedef} hedefine {Ay Yıl} civarında ulaşırsın."
