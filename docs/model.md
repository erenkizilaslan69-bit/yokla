# Veri modeli

Tüm para alanları `decimal(18,2)`. Tüm kullanıcı verisi `UserId` (Identity kullanıcısı) ile ilişkilidir.

## Category
| Alan | Tip | Not |
| --- | --- | --- |
| Id | int | |
| Name | string | |
| Type | enum (Income, Expense) | |
| IsUnnecessary | bool | Gereksiz harcama kategorisi mi (uyarı kuralı 2 için) |

Varsayılan kategoriler (seed):
- Gelir: KYK Bursu, Aile Desteği, Part-time İş, Diğer Gelir
- Gider: Market, Kira/Yurt, Faturalar, Ulaşım, Eğitim/Kırtasiye, Sağlık, Giyim, Abonelikler, Diğer Gider
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

## Goal
| Alan | Tip | Not |
| --- | --- | --- |
| Id | int | |
| UserId | string | |
| Name | string | ör. "Kulaklık" |
| TargetAmount | decimal | |
| CreatedAt | DateTime | |
| IsCompleted | bool | Katkılar toplamı hedefe ulaşınca true |

## GoalContribution
| Alan | Tip | Not |
| --- | --- | --- |
| Id | int | |
| GoalId | int | |
| Amount | decimal | |
| Date | DateTime | |
| Source | enum (MonthClose, Manual) | |

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
