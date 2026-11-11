using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace MestEQ.Core
{
    /// <summary>
    /// Başlangıç presetleri. Bunlar "temel düzeltme + zevk katmanı" üzerine binen
    /// ince dokunuşlardır; kulaklığın kusurlarını değil müziğin ihtiyacını hedefler.
    /// </summary>
    public static class DefaultPresets
    {
        public const string NeutralId = "builtin-neutral";

        /// <summary>
        /// Altruva için ölçümsüz, temkinli başlangıç: açık arkalı kulaklıklarda tipik olan alt bas
        /// düşüşünü hafifçe telafi eder, başka hiçbir yere dokunmaz. Gerçek ölçümden üretilmiş
        /// AutoEQ eğrisi içe aktarıldığında bunun yerini alır.
        /// </summary>
        public static IEnumerable<EqFilter> AltruvaEstimatedBase()
        {
            yield return new EqFilter(FilterKind.LowShelf, 55, 2.0, 0.7);
        }

        public static IEnumerable<Preset> All()
        {
            yield return Make(NeutralId, "Nötr", "Hiçbir müzik dokunuşu yok; sadece temel düzeltme ve zevk katmanı.");

            yield return Make("builtin-rock", "Rock",
                "Davulun göğüste vuruşu öne, gitar duvarının sertliği geriye. Sıkıştırılmış mixlerde ayrışmayı artırır.",
                new EqFilter(FilterKind.Peak, 80, 2.0, 0.9),
                new EqFilter(FilterKind.Peak, 320, -1.0, 1.0),
                new EqFilter(FilterKind.Peak, 3000, -1.0, 1.4),
                new EqFilter(FilterKind.HighShelf, 11000, 0.5, 0.7));

            yield return Make("builtin-metal", "Metal",
                "Çift pedal kick'e gövde, yoğun ritim gitarlarında çamuru ve fuzz sertliğini azaltır, zil tıslamasını yumuşatır. Uzun dinlemede yormaz.",
                new EqFilter(FilterKind.Peak, 60, 2.0, 0.9),
                new EqFilter(FilterKind.Peak, 250, -1.5, 1.0),
                new EqFilter(FilterKind.Peak, 2800, -1.5, 1.2),
                new EqFilter(FilterKind.Peak, 7000, -1.5, 2.5));

            yield return Make("builtin-classical", "Klasik (orkestra)",
                "Kontrbas ve viyolonsellere sıcaklık, yaylılardaki camsı parlaklığa hafif fren, salonun havasına biraz ışık.",
                new EqFilter(FilterKind.LowShelf, 120, 1.5, 0.6),
                new EqFilter(FilterKind.Peak, 2500, -0.7, 1.0),
                new EqFilter(FilterKind.HighShelf, 11000, 1.0, 0.7));

            yield return Make("builtin-jazz", "Caz",
                "Kontrbasın tellerini hissettiren alt orta, nefesli çalgıların ısırığına hafif yumuşatma, ride zilinin parıltısı.",
                new EqFilter(FilterKind.Peak, 90, 1.5, 0.8),
                new EqFilter(FilterKind.Peak, 4500, -0.8, 1.5),
                new EqFilter(FilterKind.HighShelf, 12000, 1.0, 0.7));

            yield return Make("builtin-acoustic", "Akustik ve vokal",
                "Akustik gitarın gövdesi, vokalin yakınlığı; sibilansı (sert 's' sesleri) törpüler.",
                new EqFilter(FilterKind.Peak, 200, 0.8, 0.9),
                new EqFilter(FilterKind.Peak, 3200, 1.0, 1.2),
                new EqFilter(FilterKind.Peak, 6500, -1.0, 3.0),
                new EqFilter(FilterKind.HighShelf, 12000, 1.0, 0.7));
        }

        private static Preset Make(string id, string name, string notes, params EqFilter[] filters)
        {
            return new Preset
            {
                Id = id,
                Name = name,
                Group = "Türler",
                Notes = notes,
                BuiltIn = true,
                Filters = new ObservableCollection<EqFilter>(filters)
            };
        }
    }
}
