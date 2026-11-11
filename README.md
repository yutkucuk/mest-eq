# Mest EQ

Kiwi Ears Altruva ve Moondrop Dawn Pro için yazılmış, katmanlı bir Windows ekolayzırı. Sesi arka planda Equalizer APO işler; sen sadece Mest'i görürsün.

## İlk açılış

Kurulum sırasında Equalizer APO'nun cihaz seçicisi bir kez açılır. Listede Dawn Pro'yu işaretleyip onayla. Kurulum bitince EQ etkisini duymazsan bilgisayarı bir kez yeniden başlat.

Spotify'da iki ayarı kapat: Ayarlar > Ses kalitesi > "Ses düzeyini normalleştir" ve Spotify'ın kendi ekolayzırı. Ses kalitesini en yükseğe al. Windows'ta Dawn Pro'nun "Ses geliştirmeleri" kapalı olsun.

## Nasıl çalışır

Ses dört katmandan geçer, hepsi grafikte ayrı renkle görünür:

- **Kulaklık düzeltmesi** Altruva'nın kendi frekans yanıtındaki kusurları düzeltir. Başlangıçta sadece temkinli bir alt bas desteği var. Gerçek ölçümden üretilmiş eğri için "AutoEQ / squig eğrisi yapıştır" butonuna bas; açılan rehber, earphonesarchive.squig.link'teki Altruva ölçümünden AutoEQ eğrisini nasıl kopyalayacağını gösterir.
- **Zevkin** bas, sıcaklık, varlık ve hava sürgüleriyle genel tercihin. Her preset bunun üstüne biner.
- **Preset** müziğe özel dokunuş. Rock, Metal, Klasik, Caz ve Akustik hazır gelir.
- **Loudness telafisi** kısık seste kaybolan bası ve tizi geri getirir. Normal dinleme seviyendeyken "Bu seviye normal"e bas.

Bunların üstüne crossfeed (eski stereo kayıtlarda tek kulağa yığılmış enstrümanları birleştirir) ve sol/sağ denge gelir. Clipping'i önleyen preamp her değişiklikte otomatik hesaplanır; alt bantta "Headroom" olarak görünür.

## Şarkıya göre otomatik geçiş

Spotify'da bir şarkı çalarken preset seç ve sağ alttaki "Şarkıya", "Albüme" ya da "Sanatçıya" butonlarından birine bas. O andan sonra eşleşme otomatik olur. Öncelik sırası: şarkı, albüm, sanatçı, varsayılan preset. Mest kapatıldığında sistem tepsisinde çalışmaya devam eder.

## Grafikte düzenleme

Tutamacı sürükle: yatay frekans, dikey kazanç. Tekerlek: genişlik (Q). Çift tık: yeni filtre. Sağ tık: filtreyi sil. Shift basılıyken ince ayar.

## A/B karşılaştırma

"A / B" butonu iki ayarı aynı algılanan ses seviyesinde çalar; yüksek olan her zaman daha iyi gelir yanılgısını ortadan kaldırır. "B: EQ'suz" ile ayarının gerçekten iyileştirip iyileştirmediğini, "Bunu B'ye koy" ile iki preseti karşılaştırabilirsin.

## Lisanslar

Equalizer APO GPL-2.0 lisanslıdır ve değiştirilmeden, resmi kurulum dosyasıyla kurulur. Ayrıntılar `licenses` klasöründe.
