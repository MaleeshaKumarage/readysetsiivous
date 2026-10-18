namespace CleaningSuite.Application.QualityCycle;

// Translate report labels only. Customer-entered content is preserved verbatim.
public static class ReportText
{
    private static readonly Dictionary<string, (string Fi, string Sv)> Labels = new() {
        ["Cleaning Quality Report"] = ("Siivouksen laaturaportti", "Kvalitetsrapport för städning"),
        ["No scheduled checklist shifts in this month."] = ("Ei suunniteltuja vuoroja valitulla ajanjaksolla.", "Inga planerade arbetspass under den valda perioden."),
        ["PROPERTY & SERVICE"] = ("KOHDE JA PALVELU", "FASTIGHET OCH TJÄNST"),
        ["MONTHLY SUMMARY"] = ("YHTEENVETO", "SAMMANFATTNING"),
        ["REPORT SUMMARY"] = ("RAPORTIN YHTEENVETO", "RAPPORTSAMMANFATTNING"),
        ["Reference"] = ("Viite", "Referens"),
        ["Monthly Summary Report"] = ("Kuukausiyhteenveto", "Månadssammanfattning"),
        ["Total Shift Occurrences Logged"] = ("Kirjatut vuorot yhteensä", "Totalt registrerade arbetspass"),
        ["Completed Forms"] = ("Lähetetyt lomakkeet", "Inskickade formulär"),
        ["No Quality Cycle form submissions recorded for this month."] = ("Tälle kuukaudelle ei ole kirjattu laadunvalvontalomakkeita.", "Inga kvalitetskontrollformulär har registrerats för denna månad."),
        ["Date"] = ("Päivämäärä", "Datum"), ["Cleaner"] = ("Siivooja", "Städare"),
        ["COMPLETED"] = ("VALMIS", "SLUTFÖRT"), ["PENDING"] = ("ODOTTAA", "VÄNTAR"),
        ["Checklist Items"] = ("Tarkistuslistan kohdat", "Checklistepunkter"),
        ["Completed {0} / {1} items"] = ("Merkitty tehdyksi {0} / {1} kohtaa", "{0} / {1} punkter markerade som utförda"),
        ["Cleaner Notes"] = ("Siivoojan muistiinpanot", "Städarens anteckningar"),
        ["Attached Photos: {0} photo(s) attached"] = ("Liitetyt kuvat: {0} kuvaa", "Bifogade foton: {0} foton"),
        ["Company"] = ("Yritys", "Företag"), ["Branch"] = ("Toimipiste", "Verksamhetsställe"),
        ["Address"] = ("Osoite", "Adress"), ["Shift"] = ("Vuoro", "Arbetspass"),
        ["Not recorded"] = ("Ei kirjattu", "Ej registrerat"), ["Not available"] = ("Ei saatavilla", "Ej tillgängligt"),
        ["Scheduled occurrences"] = ("Suunnitellut käynnit", "Planerade besök"),
        ["Scheduled hours"] = ("Suunnitellut tunnit", "Planerade timmar"),
        ["Recorded occurrences"] = ("Kirjatut käynnit", "Registrerade besök"),
        ["Missing"] = ("Puuttuu", "Saknas"), ["Pending"] = ("Odottaa", "Väntar"),
        ["Recorded"] = ("Kirjattu", "Registrerat"), ["Cleaner tasks"] = ("Siivoojien tehtävät", "Städarnas uppgifter"),
        ["submitted"] = ("lähetetty", "inskickade"), ["Recorded staff hours"] = ("Kirjatut työtunnit", "Registrerade personaltimmar"),
        ["Incomplete timing records"] = ("Puutteelliset aikakirjaukset", "Ofullständiga tidsregistreringar"),
        ["Scheduled hours describe visits. Recorded staff hours sum each cleaner’s actual submitted duration; incomplete timings are excluded. Missing means no submission, not a failed cleaning activity."] =
            ("Suunnitellut tunnit kuvaavat käyntejä. Kirjatut työtunnit ovat siivoojien toteutuneiden aikojen summa; puutteellisia aikakirjauksia ei lasketa. Puuttuva tarkoittaa, ettei kirjausta ole lähetetty.",
             "Planerade timmar avser besök. Registrerade personaltimmar summerar städarnas faktiska tider; ofullständiga tidsuppgifter räknas inte. Saknas betyder att ingen rapport har skickats in."),
        ["ACTIVITIES PERFORMED"] = ("TEHDYT TYÖT", "UTFÖRDA AKTIVITETER"),
        ["Activities are measured by recorded visits and distinct days. Unchecked items are not failures."] = ("Työt lasketaan kirjattujen käyntien ja eri päivien mukaan. Valitsemattomat kohdat eivät tarkoita epäonnistumista.", "Aktiviteter mäts per registrerat besök och separat dag. Omarkerade punkter är inte misslyckanden."),
        ["Activity"] = ("Työ", "Aktivitet"), ["Days"] = ("Päivät", "Dagar"), ["Occurrences"] = ("Käynnit", "Besök"),
        ["VISIT RECORD"] = ("KÄYNTITIEDOT", "BESÖKSREGISTER"),
        ["Scheduled date / start"] = ("Suunniteltu päivä / aloitus", "Planerat datum / start"),
        ["Hours"] = ("Tunnit", "Timmar"), ["Submitted / expected"] = ("Lähetetty / odotettu", "Inskickade / förväntade"),
        ["Status"] = ("Tila", "Status"), ["ACTUAL WORK TIMES"] = ("TOTEUTUNEET TYÖAJAT", "FAKTISKA ARBETSTIDER"),
        ["Actual work times: Not available for this report snapshot."] = ("Toteutuneet työajat eivät ole saatavilla tässä raportissa.", "Faktiska arbetstider är inte tillgängliga i denna rapport."),
        ["Scheduled visit"] = ("Suunniteltu käynti", "Planerat besök"), ["Actual start"] = ("Todellinen aloitus", "Faktisk start"),
        ["Actual end"] = ("Todellinen lopetus", "Faktiskt slut"), ["Staff hours"] = ("Työtunnit", "Personaltimmar"),
        ["Photos & Notes"] = ("Kuvat ja muistiinpanot", "Foton och anteckningar"),
        ["Appendix - shift photos and notes"] = ("Liite - vuorojen kuvat ja muistiinpanot", "Bilaga - foton och anteckningar från arbetspass"),
        ["Shift date"] = ("Vuoron päivämäärä", "Arbetspassets datum"), ["Photo"] = ("Kuva", "Foto"),
        ["of"] = ("/", "av"), ["Photo note"] = ("Kuvan muistiinpano", "Fotoanteckning"), ["Shift note"] = ("Vuoron muistiinpano", "Anteckning för arbetspasset"),
        ["No note recorded for this historical entry."] = ("Tähän aiempaan kirjaukseen ei ole lisätty muistiinpanoa.", "Ingen anteckning finns för denna historiska post."),
        ["Photo unavailable: the saved image is unsupported, invalid or exceeds the report image limit."] = ("Kuva ei ole saatavilla: tallennettu kuva on virheellinen, sen muotoa ei tueta tai se ylittää raportin kuvarajan.", "Fotot är inte tillgängligt: bilden är ogiltig, formatet stöds inte eller bilden överskrider rapportens bildgräns."),
        ["Generated"] = ("Luotu", "Skapad"), ["Page"] = ("Sivu", "Sida"),
        ["Please review the report and acknowledge receipt. This link expires after 30 days."] = ("Tarkista raportti ja kuittaa vastaanotto. Linkki vanhenee 30 päivän kuluttua.", "Granska rapporten och bekräfta mottagandet. Länken upphör att gälla efter 30 dagar."),
        ["Open report"] = ("Avaa raportti", "Öppna rapporten"),
        ["Your cleaning quality report is ready to review."] = ("Siivouksen laaturaporttinne on valmis tarkasteltavaksi.", "Er kvalitetsrapport för städning är klar att granska."),
        ["Reporting period"] = ("Raportointijakso", "Rapportperiod"),
        ["Included shifts"] = ("Raportin vuorot", "Arbetspass i rapporten"),
        ["What is included"] = ("Raportin sisältö", "Rapportens innehåll"),
        ["The report summarises scheduled and recorded visits, scheduled hours, recorded work hours and cleaning activities. Available photos and their notes are included in the PDF appendix."] =
            ("Raportti sisältää yhteenvedon suunnitelluista ja kirjatuista käynneistä, suunnitelluista ja kirjatuista työtunneista sekä siivoustehtävistä. Saatavilla olevat kuvat ja niiden muistiinpanot ovat PDF-raportin liitteessä.",
             "Rapporten sammanfattar planerade och registrerade besök, planerade och registrerade arbetstimmar samt städaktiviteter. Tillgängliga foton och tillhörande anteckningar finns i PDF-rapportens bilaga."),
        ["Recorded activities show what was done. Unchecked activities are not automatically treated as failures."] =
            ("Kirjatut tehtävät osoittavat tehdyt työt. Valitsemattomia tehtäviä ei automaattisesti tulkita puutteiksi.",
             "Registrerade aktiviteter visar vad som har utförts. Omarkerade aktiviteter räknas inte automatiskt som brister."),
        ["Next steps"] = ("Seuraavat vaiheet", "Nästa steg"),
        ["Open the report and review the summary."] = ("Avaa raportti ja tarkista yhteenveto.", "Öppna rapporten och granska sammanfattningen."),
        ["Download the PDF for the full details and photo appendix."] = ("Lataa PDF-raportti nähdäksesi kaikki tiedot ja kuvaliitteen.", "Ladda ner PDF-rapporten för fullständiga uppgifter och fotobilagan."),
        ["Select Acknowledge receipt on the report page to confirm you received it."] = ("Vahvista raportin vastaanotto valitsemalla raporttisivulla Kuittaa vastaanotto.", "Välj Bekräfta mottagandet på rapportsidan för att bekräfta att du har fått rapporten."),
        ["Acknowledgment confirms receipt of this report. It is not a signature or approval of its contents."] =
            ("Kuittaus vahvistaa raportin vastaanoton. Se ei ole allekirjoitus eikä sisällön hyväksyntä.",
             "Bekräftelsen gäller mottagandet av rapporten. Den är inte en underskrift eller ett godkännande av innehållet."),
        ["Link available until"] = ("Linkki on käytettävissä asti", "Länken är tillgänglig till"),
        ["If the button does not work, copy this link into your browser:"] = ("Jos painike ei toimi, kopioi tämä linkki selaimeesi:", "Om knappen inte fungerar, kopiera den här länken till din webbläsare:"),
        ["For questions about this report, contact your usual cleaning service contact."] =
            ("Jos sinulla on kysyttävää raportista, ota yhteyttä siivouspalvelusi yhteyshenkilöön.", "Kontakta din ordinarie kontaktperson för städtjänsten om du har frågor om rapporten.")
    };
    public static string Get(string language, string text)
    {
        if (language == "en") return text;
        if (language is not ("fi" or "sv")) throw new ArgumentException("Unsupported report language.", nameof(language));
        if (!Labels.TryGetValue(text, out var value))
            throw new InvalidOperationException($"Missing {language} report translation for '{text}'.");
        return language == "fi" ? value.Fi : value.Sv;
    }
}
