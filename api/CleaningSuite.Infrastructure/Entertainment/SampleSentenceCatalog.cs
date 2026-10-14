using CleaningSuite.Application.Entertainment;
using CleaningSuite.Domain.Entertainment;

namespace CleaningSuite.Infrastructure.Entertainment;

public static class SampleSentenceCatalog
{
    private static GeminiSentence S(string english, string finnish, params string[] forms) =>
        new(english, finnish, Array.AsReadOnly(forms)) { IsSample = true };

    private static readonly IReadOnlyDictionary<CefrLevel, GeminiSentence[]> Samples = new Dictionary<CefrLevel, GeminiSentence[]>
    {
        [CefrLevel.A1] = new[] {
            S("I clean the kitchen.", "Siivoan keittiön.", "siivota", "keittiö"),
            S("We drink coffee.", "Juomme kahvia.", "juoda", "kahvi"),
            S("The bus is late.", "Bussi on myöhässä.", "bussi", "olla", "myöhässä"),
            S("I need a ticket.", "Tarvitsen lipun.", "tarvita", "lippu"),
            S("The room is quiet.", "Huone on hiljainen.", "huone", "olla", "hiljainen"),
            S("We open the door.", "Avaamme oven.", "avata", "ovi"),
            S("I like this book.", "Pidän tästä kirjasta.", "pitää", "tämä", "kirja"),
            S("The train leaves soon.", "Juna lähtee pian.", "juna", "lähteä", "pian"),
            S("We work together.", "Työskentelemme yhdessä.", "työskennellä", "yhdessä"),
            S("I wash my hands.", "Pesen käteni.", "pestä", "käsi"),
            S("The shop is closed.", "Kauppa on suljettu.", "kauppa", "olla", "sulkea"),
            S("I cook dinner.", "Valmistan illallisen.", "valmistaa", "illallinen")
        },
        [CefrLevel.A2] = new[] {
            S("I cleaned the kitchen yesterday.", "Siivosin keittiön eilen.", "siivota", "keittiö", "eilen"),
            S("We are going to the shop after work.", "Menemme kauppaan töiden jälkeen.", "mennä", "kauppa", "työ", "jälkeen"),
            S("Can you open the window?", "Voitko avata ikkunan?", "voida", "avata", "ikkuna"),
            S("I have already bought the tickets.", "Olen jo ostanut liput.", "olla", "jo", "ostaa", "lippu"),
            S("The bus arrived ten minutes late.", "Bussi saapui kymmenen minuuttia myöhässä.", "bussi", "saapua", "kymmenen", "minuutti", "myöhässä"),
            S("We do not work on Sundays.", "Emme työskentele sunnuntaisin.", "työskennellä", "sunnuntai"),
            S("I would like a cup of tea.", "Haluaisin kupin teetä.", "haluta", "kuppi", "tee"),
            S("The weather was warmer last week.", "Sää oli lämpimämpi viime viikolla.", "sää", "olla", "lämmin", "viime", "viikko"),
            S("You must wash your hands before eating.", "Sinun täytyy pestä kätesi ennen ruokailua.", "täytyä", "pestä", "käsi", "ruokailu"),
            S("This room is larger than the other one.", "Tämä huone on suurempi kuin toinen.", "tämä", "huone", "olla", "suuri", "toinen"),
            S("We stayed at home because it was raining.", "Jäimme kotiin, koska satoi.", "jäädä", "koti", "sataa"),
            S("Please tell me where the station is.", "Kerro minulle, missä asema on.", "kertoa", "asema", "olla")
        },
        [CefrLevel.B1] = new[] {
            S("If it rains, we will stay indoors.", "Jos sataa, pysymme sisällä.", "sataa", "pysyä", "sisällä"),
            S("I have been learning Finnish for two years.", "Olen opiskellut suomea kaksi vuotta.", "olla", "opiskella", "suomi", "kaksi", "vuosi"),
            S("We finished cleaning before the guests arrived.", "Saimme siivouksen valmiiksi ennen kuin vieraat saapuivat.", "saada", "siivous", "valmis", "vieras", "saapua"),
            S("She asked me whether I needed help.", "Hän kysyi minulta, tarvitsinko apua.", "kysyä", "tarvita", "apu"),
            S("I would travel more if I had enough time.", "Matkustaisin enemmän, jos minulla olisi tarpeeksi aikaa.", "matkustaa", "enemmän", "olla", "tarpeeksi", "aika"),
            S("The book that you recommended was interesting.", "Kirja, jota suosittelit, oli mielenkiintoinen.", "kirja", "suositella", "olla", "mielenkiintoinen"),
            S("Although he was tired, he completed the task.", "Vaikka hän oli väsynyt, hän suoritti tehtävän loppuun.", "olla", "väsynyt", "suorittaa", "tehtävä", "loppu"),
            S("We should check the timetable before leaving.", "Meidän pitäisi tarkistaa aikataulu ennen lähtöä.", "pitää", "tarkistaa", "aikataulu", "lähtö"),
            S("I did not realize that the shop had closed.", "En tajunnut, että kauppa oli sulkeutunut.", "tajuta", "kauppa", "olla", "sulkeutua"),
            S("Please explain how this machine works.", "Selitä, miten tämä kone toimii.", "selittää", "tämä", "kone", "toimia"),
            S("By the time we arrived, dinner was ready.", "Kun saavuimme, illallinen oli jo valmis.", "saapua", "illallinen", "olla", "jo", "valmis"),
            S("They have agreed to meet again next week.", "He ovat sopineet tapaavansa uudelleen ensi viikolla.", "olla", "sopia", "tavata", "uudelleen", "ensi", "viikko")
        },
        [CefrLevel.B2] = new[] {
            S("I would have called you if I had known about the delay.", "Olisin soittanut sinulle, jos olisin tiennyt viivästyksestä.", "olla", "soittaa", "tietää", "viivästys"),
            S("The proposal was rejected despite its obvious benefits.", "Ehdotus hylättiin sen ilmeisistä eduista huolimatta.", "ehdotus", "hylätä", "ilmeinen", "etu", "huolimatta"),
            S("We need to consider how the changes will affect our customers.", "Meidän on pohdittava, miten muutokset vaikuttavat asiakkaisiimme.", "olla", "pohtia", "muutos", "vaikuttaa", "asiakas"),
            S("Having completed the inspection, she wrote a detailed report.", "Suoritettuaan tarkastuksen hän kirjoitti yksityiskohtaisen raportin.", "suorittaa", "tarkastus", "kirjoittaa", "yksityiskohtainen", "raportti"),
            S("It is essential that everyone understands the safety instructions.", "On olennaista, että kaikki ymmärtävät turvallisuusohjeet.", "olla", "olennainen", "kaikki", "ymmärtää", "turvallisuusohje"),
            S("He admitted that he had underestimated the amount of work.", "Hän myönsi aliarvioineensa työn määrän.", "myöntää", "aliarvioida", "työ", "määrä"),
            S("If the weather improves, the event is likely to take place outdoors.", "Jos sää paranee, tapahtuma järjestetään todennäköisesti ulkona.", "sää", "parantua", "tapahtuma", "järjestää", "todennäköisesti", "ulkona"),
            S("The more carefully we plan, the fewer mistakes we make.", "Mitä huolellisemmin suunnittelemme, sitä vähemmän teemme virheitä.", "huolellisesti", "suunnitella", "vähemmän", "tehdä", "virhe"),
            S("She insisted on checking the equipment before starting.", "Hän vaati laitteiden tarkistamista ennen aloittamista.", "vaatia", "laite", "tarkistaa", "aloittaa"),
            S("The results suggest that the new method is more effective.", "Tulokset viittaavat siihen, että uusi menetelmä on tehokkaampi.", "tulos", "viitata", "uusi", "menetelmä", "olla", "tehokas"),
            S("We were supposed to receive the delivery by Friday.", "Meidän oli määrä saada toimitus perjantaihin mennessä.", "olla", "määrä", "saada", "toimitus", "perjantai"),
            S("There is little point in continuing unless we agree on the goal.", "Jatkaminen ei juuri kannata, ellemme sovi tavoitteesta.", "jatkaa", "juuri", "kannattaa", "sopia", "tavoite")
        },
        [CefrLevel.C1] = new[] {
            S("Had the warning been taken seriously, the incident might have been avoided.", "Jos varoitus olisi otettu vakavasti, tapahtuma olisi saatettu välttää.", "varoitus", "olla", "ottaa", "vakavasti", "tapahtuma", "saattaa", "välttää"),
            S("The findings call into question the assumptions underlying the policy.", "Havainnot kyseenalaistavat toimintaperiaatteen taustalla olevat oletukset.", "havainto", "kyseenalaistaa", "toimintaperiaate", "tausta", "olla", "oletus"),
            S("What concerns me most is the lack of reliable evidence.", "Minua huolestuttaa eniten luotettavan näytön puute.", "huolestuttaa", "eniten", "luotettava", "näyttö", "puute"),
            S("Not only did the team meet the deadline, but it also exceeded expectations.", "Tiimi ei ainoastaan noudattanut määräaikaa, vaan myös ylitti odotukset.", "tiimi", "ainoastaan", "noudattaa", "määräaika", "myös", "ylittää", "odotus"),
            S("The agreement remains subject to approval by the relevant authorities.", "Sopimus edellyttää edelleen asianomaisten viranomaisten hyväksyntää.", "sopimus", "edellyttää", "edelleen", "asianomainen", "viranomainen", "hyväksyntä"),
            S("Given the circumstances, postponing the decision would be prudent.", "Olosuhteet huomioon ottaen päätöksen lykkääminen olisi harkittua.", "olosuhde", "huomio", "ottaa", "päätös", "lykätä", "olla", "harkittu"),
            S("She was reluctant to commit herself before the implications became clear.", "Hän ei halunnut sitoutua ennen kuin seuraukset selvisivät.", "haluta", "sitoutua", "seuraus", "selvitä"),
            S("The distinction, subtle though it may be, has significant consequences.", "Erolla, vaikka se saattaa olla hienovarainen, on merkittäviä seurauksia.", "ero", "saattaa", "olla", "hienovarainen", "merkittävä", "seuraus"),
            S("There is growing concern that short-term gains are obscuring long-term risks.", "Huoli siitä, että lyhyen aikavälin hyödyt peittävät pitkän aikavälin riskit, kasvaa.", "huoli", "lyhyt", "aikaväli", "hyöty", "peittää", "pitkä", "aikaväli", "riski", "kasvaa"),
            S("By no means should these preliminary results be regarded as conclusive.", "Näitä alustavia tuloksia ei missään tapauksessa pidä pitää ratkaisevina.", "tämä", "alustava", "tulos", "tapaus", "pitää", "ratkaiseva"),
            S("The committee sought to reconcile competing priorities without compromising quality.", "Komitea pyrki sovittamaan yhteen kilpailevat painopisteet laadusta tinkimättä.", "komitea", "pyrkiä", "sovittaa", "yhteen", "kilpailla", "painopiste", "laatu", "tinkiä"),
            S("Whatever course we choose, transparency must remain a guiding principle.", "Valitsemme minkä toimintatavan tahansa, avoimuuden on pysyttävä ohjaavana periaatteena.", "valita", "toimintatapa", "avoimuus", "olla", "pysyä", "ohjata", "periaate")
        },
        [CefrLevel.C2] = new[] {
            S("Rarely has a seemingly minor adjustment had such far-reaching implications.", "Harvoin näennäisesti vähäisellä muutoksella on ollut näin kauaskantoisia seurauksia.", "harvoin", "näennäisesti", "vähäinen", "muutos", "olla", "kauaskantoinen", "seuraus"),
            S("Be that as it may, the argument rests on an assumption that has yet to be substantiated.", "Oli miten oli, perustelu nojaa oletukseen, jota ei ole vielä osoitettu todeksi.", "olla", "perustelu", "nojata", "oletus", "vielä", "osoittaa", "tosi"),
            S("The proposal is defensible in principle, albeit considerably less so in practice.", "Ehdotus on periaatteessa puolustettavissa, joskin käytännössä huomattavasti vähemmän.", "ehdotus", "olla", "periaate", "puolustaa", "käytäntö", "huomattavasti", "vähemmän"),
            S("Had it not been for her timely intervention, matters would have taken a decidedly different turn.", "Ilman hänen oikea-aikaista puuttumistaan asiat olisivat saaneet aivan toisenlaisen käänteen.", "oikea-aikainen", "puuttua", "asia", "olla", "saada", "aivan", "toisenlainen", "käänne"),
            S("To dismiss the criticism outright would be to overlook the very issue it seeks to illuminate.", "Arvostelun torjuminen suoralta kädeltä merkitsisi juuri sen ongelman sivuuttamista, jota se pyrkii valaisemaan.", "arvostelu", "torjua", "suora", "käsi", "merkitä", "juuri", "ongelma", "sivuuttaa", "pyrkiä", "valaista"),
            S("The apparent consensus belies a profound disagreement over the underlying principles.", "Näennäinen yhteisymmärrys kätkee syvän erimielisyyden taustalla olevista periaatteista.", "näennäinen", "yhteisymmärrys", "kätkeä", "syvä", "erimielisyys", "tausta", "olla", "periaate"),
            S("Whether the measure proves proportionate hinges on considerations that are not readily quantifiable.", "Se, osoittautuuko toimenpide oikeasuhtaiseksi, riippuu seikoista, joita ei voi helposti ilmaista määrällisesti.", "osoittautua", "toimenpide", "oikeasuhtainen", "riippua", "seikka", "voida", "helposti", "ilmaista", "määrällisesti"),
            S("Insofar as the evidence permits a conclusion, it points to a more nuanced interpretation.", "Siinä määrin kuin näyttö sallii johtopäätöksen, se viittaa vivahteikkaampaan tulkintaan.", "määrä", "näyttö", "sallia", "johtopäätös", "viitata", "vivahteikas", "tulkinta"),
            S("Far from resolving the ambiguity, the clarification introduced an additional layer of uncertainty.", "Selvennys ei suinkaan ratkaissut epäselvyyttä, vaan toi mukaan uuden epävarmuuden kerroksen.", "selvennys", "suinkaan", "ratkaista", "epäselvyys", "tuoda", "mukaan", "uusi", "epävarmuus", "kerros"),
            S("The claim warrants scrutiny precisely because its intuitive appeal can mask its conceptual flaws.", "Väitettä on syytä tarkastella juuri siksi, että sen intuitiivinen vetovoima voi peittää sen käsitteelliset puutteet.", "väite", "olla", "syy", "tarkastella", "juuri", "intuitiivinen", "vetovoima", "voida", "peittää", "käsitteellinen", "puute"),
            S("However compelling the narrative, it cannot substitute for an independently verifiable account.", "Vaikka kertomus olisi kuinka vakuuttava, se ei voi korvata riippumattomasti todennettavaa selvitystä.", "kertomus", "olla", "vakuuttava", "voida", "korvata", "riippumattomasti", "todentaa", "selvitys"),
            S("The distinction is not merely semantic; it determines how responsibility is understood and assigned.", "Ero ei ole pelkästään semanttinen, vaan se määrittää, miten vastuu ymmärretään ja jaetaan.", "ero", "olla", "pelkästään", "semanttinen", "määrittää", "vastuu", "ymmärtää", "jakaa")
        }
    };

    public static GeminiSentence Select(CefrLevel level, IReadOnlyList<string> previousSentences)
    {
        if (!Samples.TryGetValue(level, out var candidates)) throw new ArgumentOutOfRangeException(nameof(level));
        ArgumentNullException.ThrowIfNull(previousSentences);
        var previous = previousSentences.Select(s => s.Trim()).ToList();
        var used = previous.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return candidates.FirstOrDefault(s => !used.Contains(s.SentenceEn))
            ?? candidates.OrderByDescending(s => previous.FindIndex(p => StringComparer.OrdinalIgnoreCase.Equals(p, s.SentenceEn))).First();
    }
}
