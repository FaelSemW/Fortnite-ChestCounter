using System.Globalization;

namespace ChestCounter;

/// <summary>
/// User interface language (Portuguese, English, Spanish or French), chosen on first launch and saved in config.json.
/// Texts are written inline as <c>L.T("français", "English", "español", "português")</c>. Names stored in data.json (chest kinds,
/// treasure items) are kept in French for compatibility and translated when displayed (<see cref="Name"/>).
/// </summary>
public static class L
{
    /// <summary>Languages offered, in menu order.</summary>
    public static readonly string[] Codes = { "pt", "en", "es", "fr" };

    /// <summary>"pt", "en", "es" or "fr".</summary>
    public static string Code { get; private set; } = "pt";

    public static bool Pt => Code == "pt";
    public static bool En => Code == "en";
    public static bool Es => Code == "es";

    public static CultureInfo Culture { get; private set; } = CultureInfo.GetCultureInfo("pt-BR");

    public static void Set(string? code)
    {
        Code = Normalize(code) ?? "pt";
        Culture = CultureInfo.GetCultureInfo(Code switch
        {
            "pt" => "pt-BR",
            "en" => "en-GB",
            "es" => "es-ES",
            _ => "fr-FR"
        });
    }

    /// <summary>"pt", "en", "es", "fr" or null when the answer is not recognized.</summary>
    public static string? Normalize(string? code) => code?.Trim().ToLowerInvariant() switch
    {
        "pt" or "por" or "portuguese" or "português" or "portugues" or "pt-br" or "pt_br" => "pt",
        "en" or "eng" or "english" or "anglais" or "inglés" or "ingles" => "en",
        "es" or "spa" or "spanish" or "español" or "espanol" or "espagnol" => "es",
        "fr" or "fra" or "fre" or "french" or "français" or "francais" or "francés" or "frances" => "fr",
        _ => null,
    };

    /// <summary>Name of a language in that language (menus).</summary>
    public static string NativeName(string code) => code switch
    {
        "pt" => "Português (Brasil)",
        "en" => "English",
        "es" => "Español",
        _ => "Français"
    };

    /// <summary>Language of Windows when it is one of ours, Portuguese otherwise if in BR, else English.</summary>
    public static string SystemDefault
    {
        get
        {
            var winLang = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
            return Normalize(winLang) ?? (winLang.Equals("pt", StringComparison.OrdinalIgnoreCase) ? "pt" : "en");
        }
    }

    public static string T(string fr, string en, string es, string? pt = null)
    {
        if (Pt)
        {
            if (pt != null) return pt;
            if (PtPhrases.TryGetValue(en, out var translated)) return translated;
            return en;
        }
        return Code switch { "en" => en, "es" => es, _ => fr };
    }

    public static string N(int n) => n.ToString("N0", Culture);

    /// <summary>"3 coffres" / "3 chests" / "3 cofres" / "3 baús".</summary>
    public static string Count(int n, string fr, string en, string es, string? pt = null) =>
        Count(n, fr, fr + "s", en, en + "s", es, es + "s", pt ?? en, pt != null ? (pt.EndsWith("s") ? pt : pt + "s") : (en + "s"));

    /// <summary>French: singular for 0 and 1. English, Spanish and Portuguese: singular for 1 only.</summary>
    public static string Count(int n, string frOne, string frMany, string enOne, string enMany, string esOne, string esMany, string? ptOne = null, string? ptMany = null) => Code switch
    {
        "pt" => $"{N(n)} {(n == 1 ? (ptOne ?? enOne) : (ptMany ?? enMany))}",
        "en" => $"{N(n)} {(n == 1 ? enOne : enMany)}",
        "es" => $"{N(n)} {(n == 1 ? esOne : esMany)}",
        _ => $"{N(n)} {(n > 1 ? frMany : frOne)}",
    };

    /// <summary>"1º" / "12th" / "12.º" / "12e".</summary>
    public static string Ordinal(int n)
    {
        if (Pt || Es)
        {
            return $"{n}º";
        }
        if (!En)
        {
            return n == 1 ? "1er" : $"{n}e";
        }
        var suffix = (n % 100) is 11 or 12 or 13 ? "th" : (n % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" };
        return $"{n}{suffix}";
    }

    /// <summary>Short date + time of a match ("05/11 22:54" / "5 Nov 22:54").</summary>
    public static string DateTimeShort(DateTime d) => d.ToString(En ? "d MMM HH:mm" : "dd/MM HH:mm", Culture);

    // French names stored in data.json -> English / Spanish / Portuguese display names.
    private static readonly Dictionary<string, (string En, string Es, string Pt)> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Coffre"] = ("Chest", "Cofre", "Baú"),
        ["Coffre rare"] = ("Rare chest", "Cofre raro", "Baú raro"),
        ["Coffre enterré"] = ("Buried chest", "Cofre enterrado", "Baú enterrado"),
        ["Coffre d'esprit (rare)"] = ("Sprite chest (rare)", "Cofre de espíritu (raro)", "Baú de espírito (raro)"),
        ["Coffre des affiches d'esprit"] = ("Sprite poster chest", "Cofre de los carteles de espíritu", "Baú de cartaz de espírito"),
        ["Autre coffre"] = ("Other chest", "Otro cofre", "Outro baú"),
        ["Canon à butin"] = ("Loot cannon", "Cañón de botín", "Canhão de saque"),
        ["Caisse de munitions"] = ("Ammo box", "Caja de munición", "Caixa de munição"),
        ["Caisse de nourriture"] = ("Food box", "Caja de comida", "Caixa de comida"),
        ["Autre conteneur"] = ("Other container", "Otro contenedor", "Outro contêiner"),
        ["Trésor"] = ("Treasure", "Tesoro", "Tesouro"),
        ["Trésor (type inconnu)"] = ("Treasure gizmo", "Artilugio de tesoro", "Dispositivo de tesouro"),
        ["Gizmo de trésor"] = ("Treasure gizmo", "Artilugio de tesoro", "Dispositivo de tesouro"),
        ["Feu d'artifice doré"] = ("Golden firework", "Fuego artificial dorado", "Fogos de artifício dourados"),
        ["Extracteur portable"] = ("Portable extractor", "Extractor portátil", "Extrator portátil"),
        ["Localisateur d'esprit"] = ("Sprite locator", "Localizador de espíritus", "Localizador de espíritos"),
        ["Localisateur chanceux"] = ("Lucky locator", "Localizador de la suerte", "Localizador de sorte"),
        ["Accélérateur d'extraction"] = ("Extraction accelerator", "Acelerador de extracción", "Acelerador de extração"),
        ["inconnu"] = ("unknown", "desconocido", "desconhecido"),
    };

    /// <summary>Display name of a chest kind or treasure item stored in French.</summary>
    public static string Name(string? stored)
    {
        if (stored != null)
        {
            stored = ChestKinds.Canonical(stored); // older name of a kind
        }
        if (string.IsNullOrEmpty(stored) || Code == "fr" || !Names.TryGetValue(stored, out var t))
        {
            return stored ?? "";
        }
        return Pt ? t.Pt : (En ? t.En : t.Es);
    }

    // Common English strings -> Portuguese translations
    private static readonly Dictionary<string, string> PtPhrases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Fortnite Chest Counter"] = "Contador de Baús Fortnite",
        ["Open the counter"] = "Abrir o contador",
        ["Open the report"] = "Abrir o relatório",
        ["Quit"] = "Sair",
        ["Settings"] = "Configurações",
        ["Your replays"] = "Seus replays",
        ["Replays folder found:"] = "✔ Pasta de replays encontrada:",
        ["Replays folder not found:"] = "✖ Pasta de replays não encontrada:",
        ["Enter to continue"] = "Enter para continuar",
        ["Analyzing your replays…"] = "Analisando seus replays…",
        ["Show an overlay in game?"] = "Exibir overlay durante a partida?",
        ["Yes, show the overlay"] = "Sim, exibir o overlay",
        ["No, only the report"] = "Não, apenas o relatório",
        ["The chest counter is already open: look in the taskbar."] = "  O contador de baús já está aberto: verifique na barra de tarefas.",
        ["(this window closes in 10 seconds)"] = "  (esta janela fecha em 10 segundos)",
        ["Unexpected error: "] = "Erro inesperado: ",
        ["Press any key to close."] = "Pressione qualquer tecla para fechar.",
        ["Live"] = "Ao vivo",
        ["In a match"] = "Em partida",
        ["Last"] = "Última",
        ["Session"] = "Sessão",
        ["Total"] = "Total",
        ["Treasures"] = "Tesouros",
        ["current match"] = "partida atual",
        ["in progress"] = "em andamento",
        ["● LIVE"] = "● AO VIVO",
        ["● MATCH IN PROGRESS"] = "● PARTIDA EM ANDAMENTO",
        ["last match"] = "última partida",
        ["avg."] = "méd.",
        ["destroyed"] = "destruído",
        ["unknown mode"] = "modo desconhecido",
        ["Ranked"] = "Ranqueada",
        ["Zero Build"] = "Construção Zero",
        ["Solo"] = "Solo",
        ["Duos"] = "Duplas",
        ["Trios"] = "Trios",
        ["Squads"] = "Esquadrões",
        ["Victory Royale!"] = "Vitória Royale!",
        ["unknown placement"] = "colocação desconhecida",
        ["time and source unknown"] = "momento e origem desconhecidos",
        ["at"] = "às",
        ["around"] = "por volta de",
        ["probable"] = "provável",
        ["Match in progress… (its chests will be counted at the end)"] = "Partida em andamento… (os baús serão computados ao término)",
        ["The report opens in your browser and fills in as the replays are read."] = "O relatório é aberto no navegador e atualizado conforme os replays são lidos.",
        ["Keep this folder"] = "Manter esta pasta",
        ["Enter another folder"] = "Informar outra pasta",
        ["Yes, analyze them now"] = "Sim, analisar agora",
        ["No, only my next matches"] = "Não, apenas minhas próximas partidas",
        ["↑ ↓ to choose, Enter to confirm"] = "↑ ↓ para escolher, Enter para confirmar",
        ["Overlay hidden: Ctrl+Shift+O (anywhere) to show it again."] = "Overlay oculto: Ctrl+Shift+O (em qualquer lugar) para reexibir.",
        ["Overlay locked: mouse passes through."] = "Overlay bloqueado: o mouse passa através.",
        ["Overlay unlocked: hold left click to move it, padlock to lock it again."] = "Overlay desbloqueado: segure o clique esquerdo para mover, clique no cadeado para travar.",
    };
}
