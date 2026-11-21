using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace OpenUtau.Plugin.Icelandic;

/// <summary>
/// Reglubundið G2P-kerfi fyrir íslensku byggt á Hljóðritaranum (Max Naylor).
/// Varpar íslenskri stafsetningu yfir í hljóðritun með kerfisbundnum hljóðreglum.
/// </summary>
public static class IcelandicRuleG2p {
    private static readonly Dictionary<string, string> Exceptions = new(StringComparer.OrdinalIgnoreCase) {
        ["guð"] = "kvʏːð",
        ["lega"] = "lɛɣa",
        ["legur"] = "lɛɣʏr",
        ["að"] = "að",
        ["af"] = "av",
        ["chili"] = "siːli",
        ["Chile"] = "siːlɛ",
        ["ennþá"] = "ɛnθau",
        ["game"] = "keim",
        ["golf"] = "kɔlf",
        ["kinn"] = "cʰɪnː",
        ["jóga"] = "jouːka",
        ["lime"] = "laiːm",
        ["skinn"] = "scɪnː",
        ["sófi"] = "souːfi",
    };

    private static readonly HashSet<string> DoubleLExceptions = new(StringComparer.OrdinalIgnoreCase) {
        "allæs",
        "allegóría",
        "ball",
        "ballaða",
        "ballans",
        "ballerína",
        "ballest",
        "ballett",
        "beryllín",
        "beryllíum",
        "bíllaus",
        "billega",
        "billegur",
        "billjard",
        "billjarður",
        "billjón",
        "bílljós",
        "bíllykill",
        "bolla",
        "bolludagur",
        "bolluvöndur",
        "brillera",
        "brilljant",
        "brilljantín",
        "brillur",
        "búlla",
        "chili",
        "chilla",
        "chilli",
        "chillí",
        "dill",
        "dollar",
        "dollaragrín",
        "dollari",
        "dúll",
        "dúlla",
        "eðallyndi",
        "fallerast",
        "fjölleikahús",
        "fjöllesinn",
        "fjölliða",
        "fjöllistahópur",
        "fjöllistamaður",
        "fjöllyndi",
        "fjöllyndur",
        "fölleitur",
        "fúllyndi",
        "fúllyndur",
        "galla",
        "gallabuxur",
        "gallaefni",
        "gallajakki",
        "gallapils",
        "gallerí",
        "galli",
        "gallon",
        "gasgrill",
        "gella",
        "getnaðarvarnapilla",
        "getnaðarvarnarpilla",
        "gilli",
        "glæsivilla",
        "górilla",
        "górilluapi",
        "grill",
        "grilla",
        "grillaður",
        "grillhanski",
        "grillkjöt",
        "grillkol",
        "grillmatur",
        "grillsósa",
        "grillstaður",
        "grillstöng",
        "grillun",
        "grillveisla",
        "grillvökvi",
        "grímuball",
        "gúllas",
        "hafvilla",
        "halldór",
        "halldóra",
        "hallelúja",
        "halló",
        "heillangur",
        "heillegur",
        "heillengi",
        "hellaður",
        "heyrúlla",
        "hlöðuball",
        "holland",
        "hollendingur",
        "hollenska",
        "hollenskur",
        "hrapallega",
        "hrapallegur",
        "húllumhæ",
        "illepur",
        "jökulleir",
        "jökullón",
        "jólaball",
        "julla",
        "kaffistell",
        "kamellía",
        "kamilla",
        "kamillute",
        "kanellengja",
        "kanillengja",
        "kantarella",
        "karamella",
        "karamellubragð",
        "karamellusósa",
        "klámbúlla",
        "klósettrúlla",
        "knall",
        "kókosbolla",
        "kolagrill",
        "kollagen",
        "kollega",
        "kollegi",
        "kollekta",
        "krulla",
        "krullaður",
        "krullujárn",
        "krúsindúlla",
        "kyrillískur",
        "lilla",
        "lillablár",
        "lilli",
        "listagallerí",
        "litapalletta",
        "lítillækka",
        "lítillækkandi",
        "lítillækkun",
        "lítillæti",
        "lítillátur",
        "lítillega",
        "lúlla",
        "lúxusvilla",
        "mállaus",
        "málleysa",
        "málleysi",
        "málleysingi",
        "malli",
        "mállýska",
        "mállýti",
        "meðallag",
        "meðallaun",
        "meðallengd",
        "mikillæti",
        "mikillátur",
        "milla",
        "millahverfi",
        "millibar",
        "milligramm",
        "millilítri",
        "millílítri",
        "millimeter",
        "millímeter",
        "millimetri",
        "millímetri",
        "millisekúnda",
        "milljarðamæringur",
        "milljarður",
        "milljón",
        "milljón",
        "milljónaborg",
        "milljónagróði",
        "milljónamæringur",
        "milljónaskuld",
        "milljónasti",
        "milljónatap",
        "milljónatjón",
        "milljónavirði",
        "milljónaþjóð",
        "mylla",
        "myllumerki",
        "myllusteinn",
        "nellika",
        "nóvella",
        "núll",
        "núlláfangi",
        "núllgildi",
        "núllpunktur",
        "orgelleikari",
        "orgelleikur",
        "palladín",
        "palladíum",
        "palleta",
        "pallíetta",
        "pastellitur",
        "penisillín",
        "pilla",
        "pólland",
        "polli",
        "rall",
        "rallí",
        "rjómabolla",
        "rollingur",
        "rúlla",
        "rúlletta",
        "rúllubaggi",
        "rúllubindivél",
        "rúllugardína",
        "rúllugjuggatjald",
        "rúllukragapeysa",
        "rúllukragi",
        "rúllupysla",
        "rúllustigi",
        "rúlluterta",
        "sællegur",
        "sællífi",
        "salmonella",
        "salmonellusýking",
        "sella",
        "sellátur",
        "sellerí",
        "sellerírót",
        "selló",
        "sellófan",
        "sellókonsert",
        "sellóleikari",
        "sellulósi",
        "semballeikari",
        "skítbillegur",
        "skjóllaus",
        "skjóllítill",
        "skolleitur",
        "skollitaður",
        "skollitur",
        "skrall",
        "skroll",
        "skrolla",
        "sólarsella",
        "sóllúga",
        "sprell",
        "sprella",
        "sprellfjörugur",
        "sprelligosi",
        "sprellikarl",
        "sprelllifandi",
        "stillans",
        "strolla",
        "svefnpilla",
        "sveitaball",
        "tarantella",
        "textíllistamaður",
        "tillærður",
        "tillaga",
        "tilleiðanlegur",
        "tilleiðast",
        "tilleiðsla",
        "tilli",
        "tillífun",
        "tillit",
        "tillitsemi",
        "tillitslaus",
        "tillitsleysi",
        "tillitssamur",
        "tillitssemi",
        "tillögugóður",
        "tillöguréttur",
        "titillag",
        "tjalli",
        "tjilla",
        "tjull",
        "tjúllaður",
        "tjúllast",
        "togararall",
        "tollering",
        "tortilla",
        "tralla",
        "trilla",
        "trilljón",
        "trillubátur",
        "trillukarl",
        "trillusjómaður",
        "trilluútgerð",
        "troll",
        "tugmilljón",
        "tugmilljónir",
        "ulla",
        "ullabjakk",
        "undirballans",
        "upprúllaður",
        "vanilla",
        "vanillín",
        "vanilludropar",
        "vanilluhringur",
        "vanilluís",
        "vanillustöng",
        "vanillusykur",
        "vantrauststillaga",
        "vatnsdeigsbolla",
        "vatnsmylla",
        "vellíðan",
        "vellíðun",
        "vellukkaður",
        "vellyktandi",
        "vellystingar",
        "verðlaunatillaga",
        "vindmylla",
        "vindmyllugarður",
        "vinningstillaga",
        "víxillán",
        "voll",
        "vorrúlla",
        "þaullesa",
        "þingsályktunartillaga",
        "þollyndi",
        "þollyndur",
        "ötullega",
    };

    private static readonly (Regex Pattern, string Replacement)[] ConsonantRules1 = [
        new(new Regex(@"l\+l"), @"lː"),
        new(new Regex(@"gʏð"), @"gvʏð"),
        new(new Regex(@"x"), @"xs"),
        new(new Regex(@"gts"), @"xs"),
        new(new Regex(@"lks"), @"ls"),
        new(new Regex(@"lkt"), @"l̥t"),
        new(new Regex(@"lsks"), @"ls"),
        new(new Regex(@"lskt"), @"lst"),
        new(new Regex(@"nks"), @"ŋ̊s"),
        new(new Regex(@"rkts"), @"r̥s"),
        new(new Regex(@"rkt"), @"r̥t"),
        new(new Regex(@"rkst"), @"st"),
        new(new Regex(@"(rr|r)ks"), @"r̥s"),
        new(new Regex(@"rpst"), @"st"),
        new(new Regex(@"rps"), @"r̥s"),
        new(new Regex(@"rsks"), @"r̥s"),
        new(new Regex(@"rskt"), @"st"),
        new(new Regex(@"rsk"), @"sk"),
        new(new Regex(@"rsk(?=ɪ|ɛ|ai|ei)"), @"sk"),
        new(new Regex(@"rpts"), @"r̥s"),
        new(new Regex(@"rpt"), @"r̥t"),
        new(new Regex(@"rsts"), @"sː"),
        new(new Regex(@"(rr|r)st"), @"st"),
        new(new Regex(@"(rr|r)ts"), @"r̥s"),
        new(new Regex(@"rr(?=s|p|t(?!n)|c|k)"), @"r"),
        new(new Regex(@"r(?=s|p|t(?!n)|c|k)"), @"r̥"),
        new(new Regex(@"rgs"), @"rs"),
        new(new Regex(@"rgt"), @"r̥t"),
        new(new Regex(@"skt"), @"st"),
        new(new Regex(@"sps"), @"sː"),
        new(new Regex(@"stk"), @"sk"),
        new(new Regex(@"sts"), @"sː"),
        new(new Regex(@"tns"), @"sː"),
        new(new Regex(@"(nn|n)sks"), @"ns"),
        new(new Regex(@"(k|kk)ts"), @"xs"),
        new(new Regex(@"sks"), @"sː"),
        new(new Regex(@"ngl"), @"ŋl"),
        new(new Regex(@"ngn"), @"ŋn"),
        new(new Regex(@"ngs"), @"ŋs"),
        new(new Regex(@"ngt(?=$|\s)"), @"ŋ̊t"),
        new(new Regex(@"ng(?!d)"), @"ŋk"),
        new(new Regex(@"nkt"), @"n̥t"),
        new(new Regex(@"(nn|n)skt"), @"nst"),
        new(new Regex(@"(?!r)(gg|g|kk|k)(?=s|t)"), @"x"),
        new(new Regex(@"agɪ"), @"aijɪ"),
        new(new Regex(@"aiɪ"), @"aijɪ"),
        new(new Regex(@"ɛgɪ"), @"eijɪ"),
        new(new Regex(@"ɛgj"), @"eij"),
        new(new Regex(@"eigj"), @"eij"),
        new(new Regex(@"aigj"), @"aij"),
        new(new Regex(@"ɔgɪ"), @"oijɪ"),
        new(new Regex(@"œgɪ"), @"œijɪ"),
        new(new Regex(@"œigɪ"), @"œijɪ"),
        new(new Regex(@"ʏgɪ"), @"ʏijɪ"),
        new(new Regex(@"tns(?=$|\s)"), @"sː"),
        new(new Regex(@"skt(?=$|\s)"), @"st"),
        new(new Regex(@"mps"), @"ms"),
        new(new Regex(@"rmt"), @"m̥t"),
        new(new Regex(@"m(?=k|p|t|c)"), @"m̥"),
        new(new Regex(@"rmd"), @"mt"),
        new(new Regex(@"rms"), @"ms"),
        new(new Regex(@"fnd(?=$|\s)"), @"mt"),
        new(new Regex(@"fns(?=$|\s)"), @"fs"),
        new(new Regex(@"fnt(?=$|\s)"), @"m̥t"),
        new(new Regex(@"fts(?=$|\s)"), @"fs"),
        new(new Regex(@"fnd"), @"mt"),
        new(new Regex(@"fnt"), @"m"),
        new(new Regex(@"tl(?=$|\s)"), @"tl̥"),
        new(new Regex(@"k(l(?=$|\s))|kk(l(?=$|\s))"), @"hkl̥"),
        new(new Regex(@"k(n(?=$|\s))|kk(n(?=$|\s))"), @"hkn̥"),
        new(new Regex(@"(g)g(?=t|s)"), @"x"),
        new(new Regex(@"(k)k(?=t|s)"), @"x"),
        new(new Regex(@"(?=r)x(?=t|s)"), @"k"),
        new(new Regex(@"kk"), @"hk"),
        new(new Regex(@"(pn|ppn)(?=$|\s)"), @"hpn̥"),
        new(new Regex(@"ttn"), @"tn"),
        new(new Regex(@"tn"), @"htn"),
        new(new Regex(@"(t)tn(?:$|\s)"), @"htn̥"),
        new(new Regex(@"(pp|p)ts"), @"fs"),
        new(new Regex(@"(?!^|\s|v)(pp|pp(?=l)|p(?=l)|(hp|p)(?=n))"), @"hp"),
        new(new Regex(@"pl(?:$|\s)"), @"pl̥"),
        new(new Regex(@"fhp"), @"fp"),
        new(new Regex(@"p(?=m)"), @"hp"),
        new(new Regex(@"t(?=m)"), @"ht"),
        new(new Regex(@"(?!^|\s)(hk|k)(?=l(?!$|\s)|m(?!$|\s)|n(?!$|\s))"), @"hk"),
        new(new Regex(@"tt|tt(?=l)|t(?=l)"), @"ht"),
        new(new Regex(@"p(?=t|k|s|sk)"), @"f"),
        new(new Regex(@"(?:^|\s)p"), @"pʰ"),
        new(new Regex(@"(?:^|\s)t(?!r)"), @"tʰ"),
        new(new Regex(@"c"), @"s"),
        new(new Regex(@"(?:^|\s)kj"), @"cʰ"),
        new(new Regex(@"(?:^|\s)k(?=ɪ|i|ɛ|ai|ei)"), @"cʰ"),
        new(new Regex(@"(?:^|\s)k(?!ɪ|i|ɛ|ai|ei)"), @"kʰ"),
        new(new Regex(@"k(?=ɪ|ɛ|ai|ei)"), @"c"),
        new(new Regex(@"ngj"), @"ɲc"),
        new(new Regex(@"(?:^|\s)k(?=ɪ|ɛ|ai|ei)"), @"cʰ"),
        new(new Regex(@"kj"), @"c"),
        new(new Regex(@"bb"), @"pː"),
        new(new Regex(@"dd"), @"tː"),
        new(new Regex(@"nnt"), @"n̥t"),
        new(new Regex(@"n(?=c)"), @"ɲ̊"),
        new(new Regex(@"nn(?=k)"), @"ŋ̊"),
        new(new Regex(@"n(?=k)"), @"ŋ̊"),
        new(new Regex(@"(gg|g)nt"), @"ŋ̊t"),
        new(new Regex(@"(knt|hknt)"), @"ŋ̊t"),
        new(new Regex(@"rnt"), @"n̥t"),
        new(new Regex(@"n(?=k(?:$|\s)|p(?:$|\s)|t(?:$|\s))"), @"n̥"),
        new(new Regex(@"n(?=p|t)"), @"n̥"),
        new(new Regex(@"rnd"), @"nt"),
        new(new Regex(@"rnsk"), @"nsk"),
        new(new Regex(@"rns"), @"r̥s"),
        new(new Regex(@"(gg|g)lt"), @"l̥t"),
        new(new Regex(@"(gg|g)nd"), @"ŋt"),
        new(new Regex(@"(gg|g)nst"), @"ŋst"),
        new(new Regex(@"(gg|g)ns"), @"xs"),
        new(new Regex(@"llr"), @"tl̥r"),
        new(new Regex(@"(ll|l)ts"), @"l̥s"),
        new(new Regex(@"ll(?=t|c|k|s)"), @"l̥"),
        new(new Regex(@"(f|g?)l(?=c|k|p|t)"), @"l̥"),
        new(new Regex(@"(ll|l)ds"), @"ls"),
        new(new Regex(@"(gg|g)ld"), @"lt"),
        new(new Regex(@"fld"), @"lt"),
        new(new Regex(@"flt"), @"l̥t"),
        new(new Regex(@"lfd"), @"lt"),
        new(new Regex(@"lft"), @"l̥t"),
        new(new Regex(@"lvr"), @"lr"),
        new(new Regex(@"lfs"), @"ls"),
        new(new Regex(@"lgd"), @"lt"),
        new(new Regex(@"lgn"), @"ln"),
        new(new Regex(@"lps"), @"ls"),
        new(new Regex(@"mb(?=d|s)"), @"m"),
        new(new Regex(@"mbt"), @"m̥t"),
        new(new Regex(@"(mm|m)ds"), @"ms"),
        new(new Regex(@"(nn|n)ds"), @"ns"),
        new(new Regex(@"ngds"), @"ŋs"),
        new(new Regex(@"ngd"), @"ŋt"),
        new(new Regex(@"ngn"), @"ŋn"),
        new(new Regex(@"ngs"), @"ŋs"),
        new(new Regex(@"d"), @"t"),
        new(new Regex(@"f(l(?=$|\s))"), @"pl̥"),
        new(new Regex(@"f(n(?=$|\s))"), @"pn̥"),
        new(new Regex(@"ff"), @"fː"),
        new(new Regex(@"rfl"), @"rtl"),
        new(new Regex(@"rfn"), @"rtn"),
        new(new Regex(@"rfs"), @"rs"),
        new(new Regex(@"rfst"), @"rst"),
        new(new Regex(@"rft"), @"r̥t"),
        new(new Regex(@"(?!^|\s)f(?=l|m|n)"), @"p"),
        new(new Regex(@"fːl"), @"fl"),
        new(new Regex(@"g(l)(?=$|\s)"), @"kl̥"),
        new(new Regex(@"nkj"), @"ɲ̊c"),
        new(new Regex(@"nk(?=ɪ|ɛ|ai|ei)"), @"ɲ̊c"),
        new(new Regex(@"(!n)g(?=t|s)|gg(?=t|s)"), @"x"),
        new(new Regex(@"gg(?=l|n)"), @"k"),
        new(new Regex(@"gg(?!ɪ)"), @"kː"),
        new(new Regex(@"gg(?=ɪ)"), @"cː"),
        new(new Regex(@"g(n)(?=$|\s)"), @"kn̥"),
        new(new Regex(@"rgð"), @"rð"),
        new(new Regex(@"rgn"), @"rtn"),
        new(new Regex(@"(?!^|\s|œ|ˈ)g(?=$|\s|ˈ|r|ð)"), @"ɣ"),
        new(new Regex(@"ag(?=a|ɔ|ʏ)"), @"aɣ"),
        new(new Regex(@"ɛg(?=a|ɔ|ʏ)"), @"ɛɣ"),
        new(new Regex(@"ɔg(?=a|ɔ|ʏ)"), @"ɔɣ"),
        new(new Regex(@"ʏg(?=a|ɔ|ʏ)"), @"ʏɣ"),
        new(new Regex(@"œg(?=a|ɔ|ʏ)"), @"œɣ"),
        new(new Regex(@"œig(?=a|ɔ|ʏ)"), @"œiɣ"),
        new(new Regex(@"ug(?=a|ʏ)"), @"uː"),
        new(new Regex(@"g(?=ɪ|i|ɛ|ai|ei)"), @"c"),
        new(new Regex(@"(?=i|u)c(?=ɪ)"), @"j"),
        new(new Regex(@"(?:^|\s)gj"), @"c"),
        new(new Regex(@"nks"), @"ŋks"),
        new(new Regex(@"(!ŋ)k(?=t|s)"), @"x"),
        new(new Regex(@"ðk"), @"θk"),
        new(new Regex(@"g"), @"k"),
        new(new Regex(@"hj"), @"ç"),
        new(new Regex(@"hl"), @"l̥"),
        new(new Regex(@"hn"), @"n̥"),
        new(new Regex(@"hr"), @"r̥"),
        new(new Regex(@"(?:^|\s)hv"), @"kʰv"),
        new(new Regex(@"hv"), @"kv"),
    ];

    private static readonly (Regex Pattern, string Replacement)[] ConsonantRules2 = [
        new(new Regex(@"ll(?:$|\s)"), @"tl̥"),
        new(new Regex(@"ll"), @"tl"),
        new(new Regex(@"mm(?=t)"), @"m̥"),
        new(new Regex(@"mm"), @"mː"),
        new(new Regex(@"b"), @"p"),
        new(new Regex(@"nn(?=t)"), @"n̥"),
        new(new Regex(@"(ð)m(?:$|\s)"), @"ðm̥"),
        new(new Regex(@"(s)m(?:$|\s)"), @"sm̥"),
        new(new Regex(@"inn(?=$|\s)"), @"itn"),
        new(new Regex(@"unn(?=$|\s)"), @"utn"),
        new(new Regex(@"ɪnn(?=$|\s)"), @"ɪn"),
        new(new Regex(@"ann(?=$|\s)"), @"anː"),
        new(new Regex(@"ɛnn(?=$|\s)"), @"nː"),
        new(new Regex(@"einn"), @"eitn"),
        new(new Regex(@"ounn"), @"outn"),
        new(new Regex(@"tn(?:$|\s)"), @"tn̥"),
        new(new Regex(@"nn(?=s)"), @"n"),
        new(new Regex(@"nn"), @"nː"),
        new(new Regex(@"r(?=k(?:$|\s)|p(?:$|\s)|t(?:$|\s))"), @"r̥"),
        new(new Regex(@"rr(?=k|s)"), @"r̥"),
        new(new Regex(@"rn(?:$|\s)"), @"rtn̥"),
        new(new Regex(@"rn"), @"rtn"),
        new(new Regex(@"rl(?:$|\s)"), @"rtl̥"),
        new(new Regex(@"rls"), @"tls"),
        new(new Regex(@"rl(?!ɛɣʏr|ɛɣa)"), @"rtl"),
        new(new Regex(@"rr"), @"rː"),
        new(new Regex(@"xsl(?:$|\s)"), @"xstl"),
        new(new Regex(@"sl(?:$|\s)"), @"stl̥"),
        new(new Regex(@"sl"), @"stl"),
        new(new Regex(@"sn(?:$|\s)"), @"stn̥"),
        new(new Regex(@"sn"), @"stn"),
        new(new Regex(@"r(sl)"), @"r̥stl"),
        new(new Regex(@"sst"), @"st"),
        new(new Regex(@"ss"), @"sː"),
        new(new Regex(@"þ"), @"θ"),
        new(new Regex(@"zz"), @"ts"),
        new(new Regex(@"z"), @"s"),
        new(new Regex(@"(?:^|\s)sl"), @"stl"),
        new(new Regex(@"ch"), @"tj"),
        new(new Regex(@"rɣ(?:$|\s)"), @"rk"),
        new(new Regex(@"rðl"), @"rtl"),
        new(new Regex(@"rðn"), @"rtn"),
        new(new Regex(@"rfð"), @"rð"),
        new(new Regex(@"r̥stl"), @"stl"),
        new(new Regex(@"r̥stn"), @"stn"),
        new(new Regex(@"ŋc"), @"ɲc"),
        new(new Regex(@"ːː"), @"ː"),
    ];

    private static readonly Regex FRuleRegex = new(@"(a|ɛ|ɪ|i|ɔ|œ|ʏ|u|l)f(?=a|ɛ|ɪ|i|ɔ|œ|ʏ|u|ð|g|j|r|$|\s|ˌ)");
    private static readonly Regex VowelLengthRegex = new(@"^(|cʰ|c|ç|f|fj|fl|flj|fr|h|j|k|kl|kr|kʰ|kʰr|kʰl|kʰr|lj|l|l̥|l̥j|m|mj|n|nj|n̥|p|pl|pʰ|pʰr|r|rj|r̥|r̥j|sc|sk|skr|sl|sp|st|stl|stn|stnj|str|strj|sv|s|tʰ|tʰr|tʰv|t|tj|tr|v|θ|θr|θrj?)(ai|au|a(?!i|u)|œi|œ(?!i)|ɛ|ei|jɛ|ɪ|i|ɔ|ou|ʏ|u)(?!cː|ðk|ðl|ðm|ðm̥|ðr|fː|fk|fl|fs|ft|hc|hf|ht|hp|hpm|hk|ɣð|ɣr|kː|kl|kn|ks|kt|lː|lc|lj|lk|ln|lp|lr|ls|lt|lv|l̥c|l̥k|l̥p|l̥s|l̥t|mː|mr|ms|mt|m̥c|m̥k|m̥p|m̥t|nː|nc|nj|nk|ns|nt|n̥k|n̥c|n̥t|ɲc|ɲ̊c|ŋ̊k|ŋ̊s|ŋ̊t|ŋc|ŋk|ŋl|ŋn|ŋs|ŋt|pː|pl|pn|ps|rː|rð|rk|rm|rst|rs|rv|rtl|r̥c|r̥k|r̥p|r̥s|r̥t|rtn|sː|sc|sk|sl|sm̥|st|stn|stn̥|tː|tl|tn|tn̥|ts|vð|vj|vk|vr|xs|xt|θk|θm|u|j|ij)");

    private static string ApplyVowels(string word) {
        word = Regex.Replace(word, @"e(?!i|y)", "ɛ");
        word = word.Replace("é", "jɛ");
        word = word.Replace("i", "ɪ");
        word = word.Replace("y", "ɪ");
        word = word.Replace("í", "i");
        word = word.Replace("ý", "i");
        word = word.Replace("o", "ɔ");
        word = word.Replace("ö", "œ");
        word = word.Replace("æ", "ai");
        word = word.Replace("au", "œi");
        word = word.Replace("u", "ʏ");
        word = word.Replace("ú", "u");
        word = word.Replace("á", "au");
        word = word.Replace("ó", "ou");
        word = word.Replace("eɪ", "ei");
        word = word.Replace("ey", "ei");
        word = Regex.Replace(word, @"a(?=ng|nk)", "au");
        word = Regex.Replace(word, @"ɛ(?=ng|nk)", "ei");
        word = Regex.Replace(word, @"ɪ(?=ng|nk)", "i");
        word = Regex.Replace(word, @"ɔ(?=ng|nk)", "ou");
        word = Regex.Replace(word, @"ʏ(?=ng|nk)", "u");
        word = Regex.Replace(word, @"œ(?=ng|nk)", "œi");
        word = word.Replace("ɔj", "oij");
        return word;
    }

    private static string ApplyFRule(string word) {
        var match = FRuleRegex.Match(word);
        if (match.Success) {
            var prefix = match.Groups[1].Value;
            var idx = match.Index;
            return string.Concat(word.AsSpan(0, idx), prefix, "v", word.AsSpan(idx + prefix.Length + 1));
        }
        return word;
    }

    private static string ApplyVowelLength(string word) {
        var match = VowelLengthRegex.Match(word);
        if (match.Success) {
            var vowelGroup = match.Groups[2];
            var idx = vowelGroup.Index;
            var len = vowelGroup.Length;
            return string.Concat(word.AsSpan(0, idx), vowelGroup.Value, "ː", word.AsSpan(idx + len));
        }
        return word;
    }

    public static string TranscribeWord(string word, string originalWord) {
        word = ApplyVowels(word);
        word = ApplyFRule(word);
        foreach (var (pattern, replacement) in ConsonantRules1) {
            word = pattern.Replace(word, replacement);
        }
        if (DoubleLExceptions.Contains(originalWord)) {
            word = word.Replace("ll", "lː");
        }
        foreach (var (pattern, replacement) in ConsonantRules2) {
            word = pattern.Replace(word, replacement);
        }
        word = ApplyVowelLength(word);
        word = word.Replace("ːː", "ː");
        return word;
    }

    public static string Transcribe(string text) {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var words = text.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var result = new List<string>();
        foreach (var rawWord in words) {
            var cleaned = Regex.Replace(rawWord, @"[,\.\?!""'„“&\u00AD]", string.Empty).Trim();
            if (cleaned.Length == 0) continue;
            var parts = cleaned.Split('-', StringSplitOptions.RemoveEmptyEntries);
            var transcribedParts = new List<string>();
            foreach (var part in parts) {
                if (Exceptions.TryGetValue(part, out var exc)) {
                    transcribedParts.Add(exc);
                } else {
                    transcribedParts.Add(TranscribeWord(part, cleaned));
                }
            }
            result.Add(string.Concat(transcribedParts));
        }
        return string.Join(" ", result);
    }

    /// <summary>
    /// Breytir IPA-streng sem Transcribe skilar í stöðluð SAMPA-hljóðritunartákn.
    /// </summary>
    public static string[] IpaToSampa(string ipa) {
        var tokens = new List<string>();
        var s = ipa.Normalize(NormalizationForm.FormC).Replace("ˈ", "").Replace("ˌ", "");
        int i = 0;
        while (i < s.Length) {
            if (s.Length - i >= 2) {
                var sub2 = s.Substring(i, 2);
                if (sub2 == "œi") { tokens.Add("9i"); i += 2; continue; }
                if (sub2 == "pʰ") { tokens.Add("p_h"); i += 2; continue; }
                if (sub2 == "tʰ") { tokens.Add("t_h"); i += 2; continue; }
                if (sub2 == "kʰ") { tokens.Add("k_h"); i += 2; continue; }
                if (sub2 == "cʰ") { tokens.Add("c_h"); i += 2; continue; }
                if (sub2 == "l̥") { tokens.Add("l_0"); i += 2; continue; }
                if (sub2 == "r̥") { tokens.Add("r_0"); i += 2; continue; }
                if (sub2 == "m̥") { tokens.Add("m_0"); i += 2; continue; }
                if (sub2 == "n̥") { tokens.Add("n_0"); i += 2; continue; }
                if (sub2 == "ɲ̊") { tokens.Add("J_0"); i += 2; continue; }
                if (sub2 == "ŋ̊") { tokens.Add("N_0"); i += 2; continue; }
                if (sub2 == "ai") { tokens.Add("ai"); i += 2; continue; }
                if (sub2 == "au") { tokens.Add("au"); i += 2; continue; }
                if (sub2 == "ei") { tokens.Add("ei"); i += 2; continue; }
                if (sub2 == "ou") { tokens.Add("ou"); i += 2; continue; }
                if (sub2 == "oi") { tokens.Add("Oi"); i += 2; continue; }
            }
            char ch = s[i];
            if (ch == 'ː' || ch == ':') {
                if (tokens.Count > 0) tokens[tokens.Count - 1] += ":";
                i++;
                continue;
            }
            switch (ch) {
                case 'a': tokens.Add("a"); break;
                case 'i': tokens.Add("i"); break;
                case 'ɪ': tokens.Add("I"); break;
                case 'ɛ': tokens.Add("E"); break;
                case 'ʏ': tokens.Add("Y"); break;
                case 'œ': tokens.Add("9"); break;
                case 'u': tokens.Add("u"); break;
                case 'ɔ': tokens.Add("O"); break;
                case 'p': tokens.Add("p"); break;
                case 't': tokens.Add("t"); break;
                case 'k': tokens.Add("k"); break;
                case 'c': tokens.Add("c"); break;
                case 'f': tokens.Add("f"); break;
                case 'v': tokens.Add("v"); break;
                case 'θ': tokens.Add("T"); break;
                case 'ð': tokens.Add("D"); break;
                case 's': tokens.Add("s"); break;
                case 'j': tokens.Add("j"); break;
                case 'ç': tokens.Add("C"); break;
                case 'ɣ': tokens.Add("G"); break;
                case 'x': tokens.Add("x"); break;
                case 'h': tokens.Add("h"); break;
                case 'm': tokens.Add("m"); break;
                case 'n': tokens.Add("n"); break;
                case 'ɲ': tokens.Add("J"); break;
                case 'ŋ': tokens.Add("N"); break;
                case 'l': tokens.Add("l"); break;
                case 'r': tokens.Add("r"); break;
                case 'w': tokens.Add("v"); break;
                case 'b': tokens.Add("p"); break;
                case 'd': tokens.Add("t"); break;
                case 'g': tokens.Add("k"); break;
                default:
                    if (!char.IsWhiteSpace(ch)) {
                        // sleppa óvörpuðum stöfum eða broddum
                    }
                    break;
            }
            i++;
        }
        return tokens.ToArray();
    }

    public static string[] TranscribeToSampa(string word) {
        var ipa = Transcribe(word);
        return IpaToSampa(ipa);
    }
}
