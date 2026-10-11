# Veri modeli

Tüm para alanları `decimal(18,2)`. Tüm kullanıcı verisi `UserId` (Identity kullanıcısı) ile ilişkilidir.

## Category
| Alan | Tip | Not |
| --- | --- | --- |
| Id | int | |
| Name | string | |
| Type | enum (Income, Expense) | |
| IsUnnecessary | bool | Gereksiz harcama kategorisi mi (uyarı kuralı 2 için) |
| IsFixed | bool | Sabit gider kategorisi mi (kira, fatura gibi); tahmin formülünde ve uyarı kuralı 1'de kullanılır |

Varsayılan kategoriler (seed):
- Gelir: KYK Bursu, Aile Desteği, Part-time İş, Diğer Gelir
- Gider: Market, Ulaşım, Eğitim/Kırtasiye, Sağlık, Giyim, Diğer Gider
- Gider (IsFixed = true): Kira/Yurt, Faturalar, Abonelikler
- Gider (IsUnnecessary = true): Dışarıda Yemek, Kafe/Kahve, Eğlence

## Transaction
| Alan | Tip | Not |
| --- | --- | --- |
| Id | int | |
| UserId | string | |
| Amount | decimal | Her zaman pozitif; yönü Type belirler |
| Type | enum (Income, Expense) | |
| Date | DateTime | |
| CategoryId | int | |
| ItemName | string? | Ürün adı (ör. "Süt 1 L"); fişten gelen satırlarda dolu |
| Note | string? | |
| RecurringTransactionId | int? | İşlem bir tekrarlayan tanımdan oluştuysa o tanım. Tanım silinince null olur (işlem kalır) |
| RecurringPeriod | DateTime? | Hangi ayın oluşumu olduğu (ayın 1'i). İşlemin tarihi değiştirilse de aynı kalır |

(RecurringTransactionId, RecurringPeriod) benzersiz olmalı: bir tanım için bir ayda en fazla bir işlem oluşur.

## RecurringTransaction
Her ay aynı gün tekrarlanan gelir veya gider tanımı (kira, burs, abonelik). İşlemleri uygulama oluşturur; kurallar için bkz. docs/rules.md bölüm 5.

| Alan | Tip | Not |
| --- | --- | --- |
| Id | int | |
| UserId | string | |
| Type | enum (Income, Expense) | |
| Amount | decimal | Her zaman pozitif |
| CategoryId | int | Türü Type ile uyumlu olmalı |
| ItemName | string? | Oluşan işlemlere kopyalanır |
| Note | string? | |
| DayOfMonth | int | 1–31; o günü olmayan aylarda ayın son günü kullanılır |
| StartDate | DateTime | Vadesi bu tarihten önce olan işlem oluşmaz. Devam ettirilince bugüne çekilir |
| EndDate | DateTime? | Vadesi bu tarihten sonra olan işlem oluşmaz |
| IsActive | bool | Kullanıcı durdurduysa false |
| LastGeneratedPeriod | DateTime? | En son işlenen ay (ayın 1'i); o ay ve öncesi yeniden oluşturulmaz |
| CreatedAt | DateTime | |

## CategoryBudget
Kullanıcının bir gider kategorisi için koyduğu aylık limit. Kurallar için bkz. docs/rules.md bölüm 6.

| Alan | Tip | Not |
| --- | --- | --- |
| Id | int | |
| UserId | string | |
| CategoryId | int | Yalnızca gider kategorisi |
| MonthlyLimit | decimal | Her zaman pozitif |
| WarnedPeriod | DateTime? | En son uyarı verilen ayın 1'i |
| WarnedLevel | enum (None, Approaching, Exceeded) | O ay verilmiş en yüksek uyarı; aynı uyarı aynı ay tekrarlanmasın diye. Limit değişince sıfırlanır |

(UserId, CategoryId) benzersiz olmalı: kategori başına tek limit.

## Goal
| Alan | Tip | Not |
| --- | --- | --- |
| Id | int | |
| UserId | string | |
| Name | string | ör. "Kulaklık" |
| TargetAmount | decimal | |
| CreatedAt | DateTime | |
| IsCompleted | bool | Katkılar toplamı hedefe ulaşınca true |
| CompletedAt | DateTime? | Hedefe ulaşıldığı gün (tamamlayan katkının tarihi); hedef açıkken null. Ana sayfadaki kutlama kartı için kullanılır |

## GoalContribution
| Alan | Tip | Not |
| --- | --- | --- |
| Id | int | |
| GoalId | int | |
| Amount | decimal | |
| Date | DateTime | |
| Source | enum (MonthClose, Manual) | Manual: kullanıcının "Para ekle" ile elle eklediği katkı. O ay yapılan Manual katkılar, ay sonu devrinde aktarılabilir tutardan düşülür |

## MonthClose
| Alan | Tip | Not |
| --- | --- | --- |
| Id | int | |
| UserId | string | |
| Year | int | |
| Month | int | |
| Remaining | decimal | O ayın gelir − gider sonucu (negatif olabilir) |
| TransferredGoalId | int? | Aktarım yapıldıysa hangi hedefe |

(UserId, Year, Month) benzersiz olmalı; aynı ay iki kez kapatılamaz.
