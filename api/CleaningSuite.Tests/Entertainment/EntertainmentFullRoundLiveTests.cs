using System.Net.Http;
using CleaningSuite.Application.Entertainment;
using CleaningSuite.Domain.Entertainment;
using CleaningSuite.Infrastructure.Entertainment;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CleaningSuite.Tests.Entertainment;

/// <summary>Explicit bounded live provider smoke. Requires ENTERTAINMENT_LIVE_SMOKE=1
/// and server-owned Gemini or OpenAI keys. Ordinary CI does not call an external provider.</summary>
[Collection(GeminiProviderCollection.Name)]
public class EntertainmentFullRoundLiveTests
{
    [Fact]
    public async Task BoundedSmoke_GeneratesAndEvaluatesOneRoundWithConfiguredProviders()
    {
        if (Environment.GetEnvironmentVariable("ENTERTAINMENT_LIVE_SMOKE") != "1") return;

        var selectedProvider = Environment.GetEnvironmentVariable("ENTERTAINMENT_LIVE_PROVIDER")?.ToLowerInvariant() ?? "both";
        var useGemini = selectedProvider is "both" or "gemini";
        var useOpenAi = selectedProvider is "both" or "openai";
        Assert.True(useGemini || useOpenAi, $"Unsupported live provider '{selectedProvider}'.");
        var values = new Dictionary<string, string?>
        {
            ["Entertainment:Gemini:ApiKey"] = useGemini ? Environment.GetEnvironmentVariable("GAME_GEMINI_API_KEY") : null,
            ["Entertainment:Gemini:Model"] = Environment.GetEnvironmentVariable("GAME_GEMINI_MODEL"),
            ["Entertainment:OpenAI:ApiKey"] = useOpenAi ? Environment.GetEnvironmentVariable("OPENAI_API_KEY") : null,
            ["Entertainment:Gemini:RateLimits:RequestsPerMinute"] = Environment.GetEnvironmentVariable("GAME_GEMINI_RPM") ?? "3",
            ["Entertainment:Gemini:RateLimits:RequestsPerDay"] = Environment.GetEnvironmentVariable("GAME_GEMINI_RPD") ?? "15",
            ["Entertainment:OpenAI:Model"] = Environment.GetEnvironmentVariable("GAME_OPENAI_MODEL") ?? "gpt-4.1-mini",
            ["Entertainment:OpenAI:RateLimits:RequestsPerMinute"] = Environment.GetEnvironmentVariable("GAME_OPENAI_RPM") ?? "5",
            ["Entertainment:OpenAI:RateLimits:RequestsPerDay"] = Environment.GetEnvironmentVariable("GAME_OPENAI_RPD") ?? "50"
        };
        if (useGemini) AddNumberedKeys(values, "Entertainment:Gemini:Keys", "GEMINI_KEY", 10);
        if (useOpenAi) AddNumberedKeys(values, "Entertainment:OpenAI:Keys", "OPENAI_API_KEY", 10);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var geminiKeys = EntertainmentAiKeyPool.Gemini(configuration);
        var openAiKeys = EntertainmentAiKeyPool.OpenAi(configuration);
        IEnumerable<ProviderKey> selectedKeys = useGemini && useOpenAi ? geminiKeys.Concat(openAiKeys) : useGemini ? geminiKeys : openAiKeys;
        Assert.NotEmpty(selectedKeys);

        using var logs = LoggerFactory.Create(builder => builder.AddConsole());
        Console.WriteLine($"Live game AI smoke: provider={selectedProvider}; Gemini keys={geminiKeys.Count}, OpenAI keys={openAiKeys.Count}; configured RPM/RPD Gemini={configuration["Entertainment:Gemini:RateLimits:RequestsPerMinute"]}/{configuration["Entertainment:Gemini:RateLimits:RequestsPerDay"]}, OpenAI={configuration["Entertainment:OpenAI:RateLimits:RequestsPerMinute"]}/{configuration["Entertainment:OpenAI:RateLimits:RequestsPerDay"]}.");
        var coordinator = new EntertainmentAiCoordinator(
            new EntertainmentAiProviderTransport(new LiveHttpClientFactory(), configuration, logs.CreateLogger<EntertainmentAiProviderTransport>()),
            configuration,
            logs.CreateLogger<EntertainmentAiCoordinator>());

        GeminiSentence round;
        try
        {
            round = await coordinator.GenerateAsync("Travel English", CefrLevel.B1, [], CancellationToken.None);
        }
        catch (EntertainmentAiUnavailableException error)
        {
            throw new Xunit.Sdk.XunitException(
                $"Question generation failed with {geminiKeys.Count} unique Gemini key(s) and {openAiKeys.Count} unique OpenAI key(s). " +
                (error.Diagnostic ?? error.Message));
        }
        Assert.False(string.IsNullOrWnõßKh‘éì¶»§q«^u½ÉÉ•Ğ¸Y…±¥…±Ñ•É¹…Ñ¥Ù”ÑÉ…¹Í±…Ñ¥½¹Ì…É”…•ÁÑ•¸Á…ÉÑ±ä…ÕÉ…Ñ”…¹Íİ•È•…É¹ÌÑ¡”Á½¥¹ÑÌ…ÍÍ¥¹•Ñ¼‰½Ñ ‘¥µ•¹Í¥½¹Ì¸I•Ù•…±¥¹œ„¡¥¹Ğ½ÈÑ¡”™Õ±°É•™•É•¹”…¹Íİ•È…İ…É‘Ì½ÈÉ•µ½Ù•Ì¹¼Á½¥¹ÑÌ¸=¹±äÑ¡”ÑÉ…¹Í±…Ñ½È•…É¹ÌÑ¡”Í½É”™½ÈÑ¡…ĞÉ½Õ¹¸()Q¡”Í•ÉÙ•ÈÁ•ÉÍ¥ÍÑÌÑ¡”ÍÕ‰µ¥ÑÑ•…¹Íİ•È‰•™½É”•Ù…±Õ…Ñ¥½¸°…¹Á•ÉÍ¥ÍÑÌ¥ÑÌ…•ÁÑ••Ù…±Õ…Ñ¥½¸İ¥Ñ Ñ¡”ÕÁ‘…Ñ•É½ÕÀÍ½É”¸%˜ÁÉ½Ù¥‘•È•Ù…±Õ…Ñ¥½¸™…¥±Ì°Ñ¡”…¹Íİ•ÈÉ•µ…¥¹Ì…Ù…¥±…‰±”…¹Ñ¡”Í…µ”É½Õ¹…¸‰”É•ÑÉ¥•¸Q¡”¹•áĞÅÕ•ÍÑ¥½¸ÍÑ…åÌ‰±½­•Õ¹Ñ¥°•Ù…±Õ…Ñ¥½¸ÍÕ••‘Ì¸I•Á•…Ñ¥¹œ„ÍÕ•ÍÍ™Õ°¡•¬É•ÑÕÉ¹ÌÑ¡”Í…Ù•É…‘”…¹‘½•Ì¹½Ğ…İ…ÉÁ½¥¹ÑÌÑİ¥”¸((ŒŒ!¥¹ÑÌ…¹…¹Íİ•ÈÙ¥Í¥‰¥±¥Ñä()Q¡”ÑÉ…¹Í±…Ñ½ÈÍ••Ì¹±¥Í İ½É‘Ì¥¸„Ñ…‰±”…¹…¸É•Ù•…°•… ¥¹¹¥Í İ½ÉÍ•Á…É…Ñ•±ä¸U¹É•Ù•…±•¥¹¹¥Í Ù…±Õ•Ì…É”½µ¥ÑÑ•™É½´Ñ¡”ÑÉ…¹Í±…Ñ½ÈÌ‰É½İÍ•ÈÁ…å±½…ìÑ¡”Í•ÉÙ•ÈÉ•ÑÕÉ¹Ì½¹”½¹±ä…™Ñ•È…¸…ÕÑ¡½É¥é•É•Ù•…°¸I•½¹¹•ĞÉ•ÍÑ½É•Ì…±É•…‘äÉ•Ù•…±•İ½É‘Ì¸=Ñ¡•ÈÁ±…å•ÉÌ…¸É•Ù•…°Ñ¡”™Õ±°É•™•É•¹”…¹Íİ•È°İ¡¥ ¥ÌÍ•¹ĞÑ¼½‰Í•ÉÙ•ÉÌ…¹¹•Ù•ÈÑ¼Ñ¡”ÑÉ…¹Í±…Ñ½È¸I•Á•…Ñ•É•Ù•…°…Ñ¥½¹Ì‘¼¹½Ğ¡…¹”Í½É•Ì¸((ŒŒM•ÉÙ•ÈÉ•‘•¹Ñ¥…°…¹ÁÉ½Ù¥‘•ÈÉ•±¥…‰¥±¥Ñä()A±…å•ÉÌ‘¼¹½Ğ•¹Ñ•ÈA$­•åÌ¸Ù•Éä…µ”•¹•É…Ñ¥½¸…¹…¹Íİ•È•Ù…±Õ…Ñ¥½¸ÁÉ½µÁĞ•¹Ñ•ÉÌ„‰½Õ¹‘•¥¸µµ•µ½Éä%<İ¥Ñ ½¹”½¹ÍÕµ•È°Í¼Ñ¡”A$ÁÉ½•ÍÍ•Ì½¹”$ÁÉ½µÁĞ…Ğ„Ñ¥µ”¸Q¡”Í•ÉÙ•ÈÑÉ¥•Ì½¹™¥ÕÉ••µ¥¹¤É•‘•¹Ñ¥…±Ì°Ñ¡•¸=Á•¹$É•‘•¹Ñ¥…±Ìİ¡•¸¹••‘•¸9Õµ‰•É•¥Ñ!ÕˆÍ•É•ÑÌ…É”5%9%}-e|Å€Ñ¡É½Õ 5%9%}-e|ÄÁ€…¹=A9%}A%}-e|Å€Ñ¡É½Õ =A9%}A%}-e|ÄÁ€ì•µÁÑäÍ±½ÑÌ…É”¥¹½É•°…¹½¹±ä½¹™¥ÕÉ•­•åÌ•¹Ñ•ÈÑ¡”Á½½°¸•µ¥¹¤±•…äÍ•É•ÑÌÉ•µ…¥¸ÍÕÁÁ½ÉÑ•¸¸Õ¹¹Õµ‰•É•=A9%}A%}-e€¥Ì…±Í¼ÍÕÁÁ½ÉÑ•…¹¥ÌÕÍ•…Ì=Á•¹$Í±½Ğ€Äİ¡•¸=A9%}A%}-e|Å€¥Ì•µÁÑä¸-•åÌ…É”ÍÑ½É•½¹±ä¥¸Ñ¡”A$¡½ÍĞÌÁ•Éµ¥ÍÍ¥½¸µÉ•ÍÑÉ¥Ñ•€¹•¹Ù€…¹Á…ÍÍ•Ñ¼Ñ¡”½¹Ñ…¥¹•È…Ğ‘•Á±½äìÑ¡•ä¹•Ù•È•¹Ñ•È‰É½İÍ•È½¹™¥ÕÉ…Ñ¥½¸°…µ”É•½É‘Ì°±½Ì°½ÈÕÍ•È¥¹ÁÕĞ¸=Á•¹$ÕÍ•Ì5}=A9%}5=1€İ¡•¸½¹™¥ÕÉ•…¹‘•™…Õ±ÑÌÑ¼ÁĞ´Ğ¸Äµµ¥¹¥€¸()AÉ½Ù¥‘•ÈÅÕ½Ñ…Ì…É”Í¡…É•…É½ÍÌ­•åÌ™½ÈÑ¡”Í…µ”ÁÉ½Ù¥‘•È½µ½‘•°°Í¼…‘‘¥¹œ­•åÌ‘½•Ì¹½Ğ…‘IA4½ÈIA¸Q¡”½½É‘¥¹…Ñ½ÈÍµ½½Ñ¡ÌÉ•ÅÕ•ÍÑÌÑ¼…Ğµ½ÍĞ½¹”É•ÅÕ•ÍĞÁ•È½¹™¥ÕÉ•¥¹Ñ•ÉÙ…°…¹•¹™½É•Ì„É½±±¥¹œ€ÈĞµ¡½ÕÈ‘…¥±ä‰Õ‘•Ğ¸UÍ…”Ñ¥µ•ÍÑ…µÁÌ…É”ÍÑ½É•¥¸Ñ¡”•á¥ÍÑ¥¹œ‘…Ñ…‰…Í”Í¼A$É•ÍÑ…ÉÑÌ‘¼¹½ĞÉ•Í•ĞÑ¡…Ğ‰Õ‘•Ğ¸•™…Õ±ÑÌ…É”•µ¥¹¤€ÌIA4€¼€ÄÔÉ•ÅÕ•ÍÑÌÁ•È€ÈĞ¡½ÕÉÌ…¹=Á•¹$€ÔIA4€¼€ÔÀÉ•ÅÕ•ÍÑÌÁ•È€ÈĞ¡½ÕÉÌ¸M•Ğ¥Ñ!ÕˆÉ•Á½Í¥Ñ½ÉäÙ…É¥…‰±•Ì5}5%9%}IA5€°5}5%9%}IA€°5}=A9%}IA5€°…¹5}=A9%}IA€Ñ¼±½İ•ÈÙ…±Õ•Ì¥˜Ñ¡”…½Õ¹Ğ‘…Í¡‰½…ÉÉ•ÅÕ¥É•Ì¥ĞìÍ•Ğ¡¥¡•ÈÙ…±Õ•Ì½¹±ä…™Ñ•È¡•­¥¹œÑ¡”•á…Ğµ½‘•°½…½Õ¹ĞÅÕ½Ñ…Ì¸Q¡”ÕÉÉ•¹Ğ=Á•¹$É•ÍÁ½¹Í”É…Ñ”µ±¥µ¥Ğ¡•…‘•ÉÌ…É”É•½É‘•¥¸A$±½Ìİ¡•¸Ñ¡”ÁÉ½Ù¥‘•ÈÉ•ÑÕÉ¹ÌÑ¡•´¸ÁÉ½Ù¥‘•È!QQ@€ĞÈä½½±Ì‘½İ¸Ñ¡…ĞÁÉ½Ù¥‘•È½µ½‘•°…¹Í­¥ÁÌ¥ÑÌ½Ñ¡•È­•åÌ°¡½¹½É¥¹œI•ÑÉäµ™Ñ•É€İ¡•¸ÁÉ•Í•¹Ğ°Ñ¡•¸±•ÑÌÑ¡”½Ñ¡•ÈÁÉ½Ù¥‘•ÈÑÉä¸Q¡¥Ì…Ù½¥‘Ì‰ÕÉ¹¥¹œ…‘‘¥Ñ¥½¹…°…±±Ì……¥¹ÍĞ„Í¡…É•ÅÕ½Ñ„¸()Q¡•É”¥Ì¹¼ÉÕ¹Ñ¥µ”Í…µÁ±”™…±±‰…¬…¹¹¼Õ¹Ù•É¥™¥•±½…°½ÈÁ±…å•ÈÉ…‘¥¹œ¸AÉ½Ù¥‘•ÈÉ•ÅÕ•ÍÑÌ¡…Ù”‰½Õ¹‘•Ñ¥µ•½ÕÑÌ¸%˜•¹•É…Ñ¥½¸½È•Ù…±Õ…Ñ¥½¸ÍÑ¥±°™…¥±Ì°Ñ¡”…µ”­••ÁÌ¥ÑÌÕÉÉ•¹ĞÉ½Õ¹…¹…±±½İÌÉ•ÑÉä¸I½ÕÑ¥¹”$ÕÍ•Ì‘•Ñ•Éµ¥¹¥ÍÑ¥ŒÁÉ½Ù¥‘•È™…­•Ì…¹‘½•Ì¹½Ğ½¹ÍÕµ”½İ¹•È­•åÌ¸Q¡”µ…¹Õ…±±äÍÑ…ÉÑ•…µ”µ…¤µ±¥Ù”µÍµ½­•€İ½É­™±½Ü•á•É¥Í•Ì•¹•É…Ñ¥½¸…¹•Ù…±Õ…Ñ¥½¸İ¥Ñ „Í•±•Ñ•Í•ÉÙ•ÈÁÉ½Ù¥‘•È€¡½Á•¹…¥€°•µ¥¹¥€°½È‰½Ñ¡€¤…¹ÁÉ¥¹ÑÌ½‰Í•ÉÙ•=Á•¹$É•ÅÕ•ÍĞµ±¥µ¥Ğ¡•…‘•ÉÌİ¡•¸É•ÑÕÉ¹•¸()½½±”…ÁÁ±¥•Ì•µ¥¹¤±¥µ¥ÑÌ‰äÁÉ½©•Ğ°…¹Ñ¡”ÕÉÉ•¹ĞÑ¥•È½µ½‘•°±¥µ¥ÑÌ…É”Í¡½İ¸¥¸$MÑÕ‘¥¼¸=Á•¹$±¥µ¥ÑÌÙ…Éä‰ä½É…¹¥é…Ñ¥½¸°ÁÉ½©•Ğ°µ½‘•°…¹ÕÍ…”Ñ¥•Èì€ĞÈäÉ•ÍÁ½¹Í•Ì…¸…±Í¼µ•…¸„‰¥±±¥¹œ½ÈÍÁ•¹±¥µ¥ĞÉ…Ñ¡•ÈÑ¡…¸IA4¸M•”m•µ¥¹¤É…Ñ”±¥µ¥ÑÍt¡¡ÑÑÁÌè¼½…¤¹½½±”¹‘•Ø½•µ¥¹¤µ…Á¤½‘½Ì½É…Ñ”µ±¥µ¥ÑÌ¤…¹m=Á•¹$Õ¥‘…¹”™½ÈÉ…Ñ”±¥µ¥ÑÌ…¹€ĞÈä•ÉÉ½ÉÍt¡¡ÑÑÁÌè¼½¡•±À¹½Á•¹…¤¹½´½•¸½…ÉÑ¥±•Ì¼ÔäÔÔØÀĞµÑÉ½Õ‰±•Í¡½½Ñ¥¹œµ…Á¤µÉ…Ñ”µ±¥µ¥ÑÌµ…¹´ĞÈäµ•ÉÉ½ÉÌ¤¸()‘‘¥¹œÍ•Ù•É…°­•åÌ™É½´Ñ¡”Í…µ”½½±”±½ÕÁÉ½©•Ğ‘½•Ì¹½ĞÉ•…Ñ”Í•Á…É…Ñ”ÅÕ½Ñ„Á½½±Ìì•µ¥¹¤±¥µ¥ÑÌ…ÁÁ±ä…ĞÑ¡”ÁÉ½©•Ğ±•Ù•°¸I½Ñ…Ñ¥½¸…¸µ½Ù”Ñ¼„‘¥™™•É•¹ĞÁÉ½Ù¥‘•È½È¥¹‘•Á•¹‘•¹Ñ±ä±¥µ¥Ñ•É•‘•¹Ñ¥…°°‰ÕĞ¥Ğ‘½•Ì¹½ĞµÕ±Ñ¥Á±ä„Í¡…É•ÁÉ½©•ĞÌÅÕ½Ñ„¸((ŒŒQ•ÍĞ½Ù•É…”()…Á¤½±•…¹¥¹MÕ¥Ñ”¹Q•ÍÑÌ½¹Ñ•ÉÑ…¥¹µ•¹Ğ½¹Ñ•ÉÑ…¥¹µ•¹ÑM½É¥¹Q•ÍÑÌ¹Í€¡•­ÌÍ½É”‰½Õ¹‘Ì°Ñ½Ñ…±Ì…¹¥¹½¹Í¥ÍÑ•¹ĞÉ…‘•Ì¸•µ¥¹¥¹Íİ•ÉÙ…±Õ…Ñ½ÉQ•ÍÑÍ€½Ù•ÉÌ…±Ñ•É¹…Ñ”…¹Íİ•ÉÌ°É…µµ…È½ÉÉ•Ñ¥½¹Ìİ¥Ñ ÁÉ•Í•ÉÙ•µ•…¹¥¹œ°¥¹½ÉÉ•Ğ…¹Íİ•ÉÌ…¹¥¹Ù…±¥ÁÉ½Ù¥‘•È½ÕÑÁÕĞ¸¹Ñ•ÉÑ…¥¹µ•¹Ñ!Õ‰%¹Ñ•É…Ñ¥½¹Q•ÍÑÍ€•á•É¥Í•ÌÉ•…°M¥¹…±H¥‘•¹Ñ¥Ñä°É•½¹¹•Ğ°É•ÑÉä°Í½É”Á•ÉÍ¥ÍÑ•¹”…¹¥‘•µÁ½Ñ•¹ĞÑÉ…¹Í¥Ñ¥½¹Ì¸Í¥Ñ”½”É”½•¹Ñ•ÉÑ…¥¹µ•¹Ğµ…µ”¹ÍÁ•Œ¹ÑÍ€¡•­ÌÑ¡”Ù¥Í¥‰±”Í½É”‰É•…­‘½İ¸…±½¹Í¥‘”™••‘‰…¬…¹É•ÑÉä‰•¡…Ù¥½È¸A½ÍÑÉ•ME0É•Í•ÉÙ…Ñ¥½¸Ñ•ÍÑÌ½Ù•È¡¥ÍÑ½ÉäÕ¹¥ÅÕ•¹•ÍÌ…¹½¹ÕÉÉ•¹ä¥¸$¸()Q¡”ÁÉ•Ù¥½ÕÌÍ…µÁ±”µ…Ñ…±½Õ”‰•¡…Ù¥½È¥ÌÉ•Ñ¥É•¸…µ”µÍ…µÁ±”µ™…±±‰…¬¹µ‘€¥ÌÍÕÁ•ÉÍ•‘•‰äÑ¡¥ÌÁ…”¸(