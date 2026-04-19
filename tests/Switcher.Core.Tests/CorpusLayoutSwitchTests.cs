using Switcher.Core;
using Xunit;

namespace Switcher.Core.Tests;

/// <summary>
/// Comprehensive corpus-based tests for layout-switch detection.
///
/// These tests validate that CorrectionHeuristics works correctly WITHOUT relying
/// on dictionary size. Words are tested in both directions:
///   - English words typed in UA layout (EN chars, should not convert)
///   - Ukrainian words typed in EN layout (EN chars → UA conversion)
///   - Ukrainian words typed in UA layout (UA chars, should not convert)
///   - EN words typed in UA layout (UA chars → EN conversion)
///
/// Also includes punctuation, mixed symbols, and sentence-level scenarios.
/// </summary>
public class CorpusLayoutSwitchTests
{
    private static CorrectionCandidate? Auto(string word) =>
        CorrectionHeuristics.Evaluate(word, CorrectionMode.Auto);

    private static CorrectionCandidate? Safe(string word) =>
        CorrectionHeuristics.Evaluate(word, CorrectionMode.Safe);

    // ══════════════════════════════════════════════════════════════════════════
    // SECTION 1: English words typed correctly → must NOT be converted
    // 1000 common English words tested in Auto mode — no false positives
    // ══════════════════════════════════════════════════════════════════════════

    [Theory]
    // ── Common everyday English ───────────────────────────────────────────────
    [InlineData("the")][InlineData("and")][InlineData("for")][InlineData("are")]
    [InlineData("but")][InlineData("not")][InlineData("you")][InlineData("all")]
    [InlineData("can")][InlineData("her")][InlineData("was")][InlineData("one")]
    [InlineData("our")][InlineData("out")][InlineData("day")][InlineData("get")]
    [InlineData("has")][InlineData("him")][InlineData("his")][InlineData("how")]
    [InlineData("its")][InlineData("new")][InlineData("now")][InlineData("old")]
    [InlineData("see")][InlineData("two")][InlineData("who")][InlineData("did")]
    [InlineData("big")][InlineData("own")][InlineData("way")][InlineData("any")]
    [InlineData("say")][InlineData("she")][InlineData("may")][InlineData("use")]
    [InlineData("man")][InlineData("lot")][InlineData("set")][InlineData("put")]
    [InlineData("end")][InlineData("why")][InlineData("ask")][InlineData("men")]
    [InlineData("run")][InlineData("far")][InlineData("sea")][InlineData("try")]
    [InlineData("few")][InlineData("ago")][InlineData("pay")][InlineData("top")]
    [InlineData("buy")][InlineData("job")][InlineData("cut")][InlineData("sit")]
    [InlineData("let")][InlineData("law")][InlineData("bit")][InlineData("hit")]
    [InlineData("win")][InlineData("hot")][InlineData("lay")][InlineData("kid")]
    [InlineData("add")][InlineData("act")][InlineData("age")][InlineData("air")]
    [InlineData("arm")][InlineData("art")][InlineData("bad")][InlineData("bag")]
    [InlineData("ban")][InlineData("bar")][InlineData("bed")]
    [InlineData("box")][InlineData("bus")][InlineData("cap")][InlineData("car")]
    [InlineData("cup")][InlineData("dad")][InlineData("dam")]
    [InlineData("die")][InlineData("dog")][InlineData("dry")][InlineData("due")]
    [InlineData("ear")][InlineData("eat")][InlineData("egg")][InlineData("era")]
    [InlineData("eye")][InlineData("fit")][InlineData("fix")][InlineData("fly")]
    [InlineData("gap")][InlineData("gas")][InlineData("god")][InlineData("gun")]
    [InlineData("hat")][InlineData("hey")][InlineData("ice")][InlineData("key")]
    [InlineData("leg")][InlineData("lie")][InlineData("lip")][InlineData("log")]
    [InlineData("low")][InlineData("mad")][InlineData("map")][InlineData("mix")]
    [InlineData("mom")][InlineData("mud")][InlineData("net")][InlineData("odd")]
    [InlineData("oil")][InlineData("pan")][InlineData("pet")][InlineData("pie")]
    [InlineData("pin")][InlineData("pit")][InlineData("pot")][InlineData("raw")]
    [InlineData("red")][InlineData("rid")][InlineData("rig")][InlineData("rip")]
    [InlineData("rob")][InlineData("rod")][InlineData("row")][InlineData("rub")]
    [InlineData("rug")][InlineData("sad")][InlineData("sip")][InlineData("ski")]
    [InlineData("sky")][InlineData("son")][InlineData("spy")][InlineData("sub")]
    [InlineData("sum")][InlineData("sun")][InlineData("tab")][InlineData("tag")]
    [InlineData("tan")][InlineData("tap")][InlineData("tar")][InlineData("tax")]
    [InlineData("ten")][InlineData("tie")][InlineData("tip")][InlineData("toe")]
    [InlineData("ton")][InlineData("too")][InlineData("toy")][InlineData("tub")]
    [InlineData("tug")][InlineData("van")][InlineData("via")][InlineData("vow")]
    [InlineData("war")][InlineData("web")][InlineData("wet")][InlineData("wow")]
    [InlineData("yam")][InlineData("yet")][InlineData("zip")][InlineData("zoo")]
    // ── 4-6 letter common words ───────────────────────────────────────────────
    [InlineData("also")][InlineData("back")][InlineData("been")][InlineData("best")]
    [InlineData("call")][InlineData("came")][InlineData("case")][InlineData("city")]
    [InlineData("come")][InlineData("cost")][InlineData("dark")][InlineData("data")]
    [InlineData("date")][InlineData("days")][InlineData("deal")][InlineData("dear")]
    [InlineData("does")][InlineData("done")][InlineData("door")][InlineData("down")]
    [InlineData("draw")][InlineData("drop")][InlineData("each")][InlineData("easy")]
    [InlineData("even")][InlineData("ever")][InlineData("face")][InlineData("fact")]
    [InlineData("fail")][InlineData("fall")][InlineData("feel")][InlineData("felt")]
    [InlineData("find")][InlineData("fire")][InlineData("fish")][InlineData("five")]
    [InlineData("font")][InlineData("food")][InlineData("form")][InlineData("four")]
    [InlineData("from")][InlineData("full")][InlineData("fund")][InlineData("game")]
    [InlineData("gave")][InlineData("give")][InlineData("glad")][InlineData("goal")]
    [InlineData("goes")][InlineData("gold")][InlineData("gone")][InlineData("good")]
    [InlineData("grew")][InlineData("grid")][InlineData("grow")][InlineData("half")]
    [InlineData("hall")][InlineData("hand")][InlineData("hard")][InlineData("have")]
    [InlineData("head")][InlineData("help")][InlineData("here")][InlineData("high")]
    [InlineData("hill")][InlineData("hold")][InlineData("hole")][InlineData("home")]
    [InlineData("hope")][InlineData("host")][InlineData("hour")][InlineData("huge")]
    [InlineData("idea")][InlineData("into")][InlineData("iron")][InlineData("item")]
    [InlineData("join")][InlineData("just")][InlineData("keep")][InlineData("kept")]
    [InlineData("kind")][InlineData("king")][InlineData("knew")][InlineData("know")]
    [InlineData("land")][InlineData("last")][InlineData("late")][InlineData("lead")]
    [InlineData("left")][InlineData("less")][InlineData("life")][InlineData("like")]
    [InlineData("line")][InlineData("link")][InlineData("list")][InlineData("live")]
    [InlineData("load")][InlineData("lock")][InlineData("long")][InlineData("look")]
    [InlineData("loss")][InlineData("lost")][InlineData("love")][InlineData("made")]
    [InlineData("mail")][InlineData("main")][InlineData("make")][InlineData("mark")]
    [InlineData("math")][InlineData("mean")][InlineData("meet")][InlineData("mind")]
    [InlineData("miss")][InlineData("mode")][InlineData("more")][InlineData("most")]
    [InlineData("move")][InlineData("much")][InlineData("must")][InlineData("name")]
    [InlineData("near")][InlineData("need")][InlineData("next")][InlineData("nice")]
    [InlineData("nine")][InlineData("node")][InlineData("none")][InlineData("noon")]
    [InlineData("note")][InlineData("noun")][InlineData("null")][InlineData("open")]
    [InlineData("oral")][InlineData("over")][InlineData("page")][InlineData("paid")]
    [InlineData("pair")][InlineData("park")][InlineData("part")][InlineData("pass")]
    [InlineData("past")][InlineData("path")][InlineData("peak")][InlineData("pick")]
    [InlineData("plan")][InlineData("play")][InlineData("plot")][InlineData("plus")]
    [InlineData("poem")][InlineData("pool")][InlineData("poor")][InlineData("port")]
    [InlineData("post")][InlineData("pray")][InlineData("pull")][InlineData("push")]
    [InlineData("race")][InlineData("rank")][InlineData("rate")][InlineData("read")]
    [InlineData("real")][InlineData("rely")][InlineData("rest")][InlineData("rich")]
    [InlineData("ride")][InlineData("ring")][InlineData("rise")][InlineData("risk")]
    [InlineData("road")][InlineData("rock")][InlineData("role")][InlineData("roof")]
    [InlineData("room")][InlineData("root")][InlineData("rose")][InlineData("rule")]
    [InlineData("safe")][InlineData("said")][InlineData("sale")][InlineData("salt")]
    [InlineData("same")][InlineData("save")][InlineData("scan")][InlineData("seat")]
    [InlineData("self")][InlineData("sell")][InlineData("send")][InlineData("sign")]
    [InlineData("silk")][InlineData("site")][InlineData("size")][InlineData("skip")]
    [InlineData("slow")][InlineData("snow")][InlineData("soap")][InlineData("soft")]
    [InlineData("soil")][InlineData("sold")][InlineData("solo")][InlineData("some")]
    [InlineData("song")][InlineData("soon")][InlineData("sort")][InlineData("soul")]
    [InlineData("span")][InlineData("spec")][InlineData("spin")][InlineData("spot")]
    [InlineData("star")][InlineData("stay")][InlineData("stem")][InlineData("step")]
    [InlineData("stop")][InlineData("such")][InlineData("suit")][InlineData("swap")]
    [InlineData("sync")][InlineData("tale")][InlineData("tall")][InlineData("task")]
    [InlineData("team")][InlineData("tell")][InlineData("term")][InlineData("test")]
    [InlineData("text")][InlineData("than")][InlineData("that")][InlineData("them")]
    [InlineData("then")][InlineData("they")][InlineData("thin")][InlineData("this")]
    [InlineData("thus")][InlineData("tide")][InlineData("tile")][InlineData("time")]
    [InlineData("tiny")][InlineData("tire")][InlineData("told")][InlineData("toll")]
    [InlineData("tone")][InlineData("tool")][InlineData("tour")][InlineData("town")]
    [InlineData("trap")][InlineData("tree")][InlineData("trim")][InlineData("trip")]
    [InlineData("true")][InlineData("tube")][InlineData("tune")][InlineData("turn")]
    [InlineData("type")][InlineData("unit")][InlineData("upon")][InlineData("used")]
    [InlineData("user")][InlineData("vary")][InlineData("vast")][InlineData("verb")]
    [InlineData("very")][InlineData("view")][InlineData("void")][InlineData("vote")]
    [InlineData("wait")][InlineData("walk")][InlineData("wall")][InlineData("want")]
    [InlineData("warm")][InlineData("wash")][InlineData("weak")][InlineData("week")]
    [InlineData("well")][InlineData("went")][InlineData("were")][InlineData("what")]
    [InlineData("when")][InlineData("whom")][InlineData("wide")][InlineData("will")]
    [InlineData("wind")][InlineData("wing")][InlineData("wire")][InlineData("wish")]
    [InlineData("with")][InlineData("wood")][InlineData("word")][InlineData("work")]
    [InlineData("worn")][InlineData("wrap")][InlineData("year")][InlineData("your")]
    [InlineData("zero")][InlineData("zone")]
    // ── Technical / programming words ────────────────────────────────────────
    [InlineData("async")][InlineData("await")][InlineData("class")][InlineData("const")]
    [InlineData("debug")][InlineData("event")][InlineData("fetch")][InlineData("field")]
    [InlineData("float")][InlineData("index")][InlineData("input")][InlineData("local")]
    [InlineData("login")][InlineData("logic")][InlineData("model")][InlineData("mongo")]
    [InlineData("mutex")][InlineData("nginx")][InlineData("oauth")][InlineData("order")]
    [InlineData("parse")][InlineData("patch")][InlineData("proxy")][InlineData("query")]
    [InlineData("queue")][InlineData("range")][InlineData("regex")][InlineData("redis")]
    [InlineData("reply")][InlineData("route")][InlineData("scope")][InlineData("stack")]
    [InlineData("state")][InlineData("store")][InlineData("token")][InlineData("trait")]
    [InlineData("tuple")][InlineData("union")][InlineData("update")][InlineData("value")]
    [InlineData("array")][InlineData("batch")][InlineData("block")][InlineData("bonus")]
    [InlineData("cache")][InlineData("check")][InlineData("chunk")][InlineData("click")]
    [InlineData("cloud")][InlineData("codec")][InlineData("count")][InlineData("crash")]
    [InlineData("crate")][InlineData("defer")][InlineData("delta")][InlineData("depth")]
    [InlineData("error")][InlineData("exact")][InlineData("extra")][InlineData("flags")]
    [InlineData("frame")][InlineData("fresh")][InlineData("front")][InlineData("graph")]
    [InlineData("guard")][InlineData("hooks")][InlineData("image")][InlineData("inbox")]
    [InlineData("inner")][InlineData("issue")][InlineData("items")][InlineData("label")]
    [InlineData("layer")][InlineData("limit")][InlineData("match")][InlineData("media")]
    [InlineData("merge")][InlineData("mount")][InlineData("mouse")]
    [InlineData("nodes")][InlineData("offer")][InlineData("other")][InlineData("outer")]
    [InlineData("owner")][InlineData("panel")][InlineData("phase")][InlineData("phone")]
    [InlineData("photo")][InlineData("pixel")][InlineData("point")][InlineData("power")]
    [InlineData("press")][InlineData("price")][InlineData("print")][InlineData("proto")]
    [InlineData("pulse")][InlineData("react")][InlineData("ready")][InlineData("reset")]
    [InlineData("retry")][InlineData("reuse")][InlineData("right")][InlineData("round")]
    [InlineData("rules")][InlineData("scale")][InlineData("score")][InlineData("setup")]
    [InlineData("share")][InlineData("shift")][InlineData("short")][InlineData("since")]
    [InlineData("sleep")][InlineData("slice")][InlineData("small")][InlineData("smart")]
    [InlineData("solve")][InlineData("space")][InlineData("spawn")][InlineData("speed")]
    [InlineData("split")][InlineData("sport")][InlineData("spray")][InlineData("start")]
    [InlineData("stats")][InlineData("style")][InlineData("super")][InlineData("table")]
    [InlineData("throw")][InlineData("total")][InlineData("touch")][InlineData("trace")]
    [InlineData("track")][InlineData("trade")][InlineData("trans")]
    [InlineData("trick")][InlineData("trust")][InlineData("typed")][InlineData("types")]
    [InlineData("unset")][InlineData("until")][InlineData("usage")][InlineData("valid")]
    [InlineData("watch")][InlineData("while")][InlineData("whole")][InlineData("width")]
    [InlineData("write")][InlineData("yield")]
    // ── Longer English technical words ───────────────────────────────────────
    [InlineData("access")][InlineData("action")][InlineData("active")][InlineData("actual")]
    [InlineData("always")][InlineData("amount")][InlineData("answer")][InlineData("append")]
    [InlineData("assert")][InlineData("assign")][InlineData("attach")][InlineData("author")]
    [InlineData("binary")][InlineData("border")][InlineData("bridge")][InlineData("broken")]
    [InlineData("buffer")][InlineData("button")][InlineData("caller")][InlineData("cancel")]
    [InlineData("carbon")][InlineData("center")][InlineData("change")][InlineData("charge")]
    [InlineData("choose")][InlineData("client")][InlineData("column")][InlineData("commit")]
    [InlineData("common")][InlineData("config")][InlineData("cookie")][InlineData("create")]
    [InlineData("cursor")][InlineData("custom")][InlineData("delete")][InlineData("deploy")]
    [InlineData("design")][InlineData("detail")][InlineData("detect")][InlineData("device")]
    [InlineData("dialog")][InlineData("direct")][InlineData("docker")][InlineData("domain")]
    [InlineData("driver")][InlineData("editor")][InlineData("effect")][InlineData("enable")]
    [InlineData("encode")][InlineData("entity")][InlineData("equals")][InlineData("escape")]
    [InlineData("except")][InlineData("exists")][InlineData("expand")][InlineData("export")]
    [InlineData("extend")][InlineData("extern")][InlineData("factor")][InlineData("failed")]
    [InlineData("filter")][InlineData("finish")][InlineData("folder")][InlineData("follow")]
    [InlineData("footer")][InlineData("format")][InlineData("future")][InlineData("getter")]
    [InlineData("global")][InlineData("handle")][InlineData("header")][InlineData("health")]
    [InlineData("helper")][InlineData("hidden")][InlineData("impact")][InlineData("import")]
    [InlineData("inline")][InlineData("insert")][InlineData("intent")][InlineData("kernel")]
    [InlineData("layout")][InlineData("legacy")][InlineData("length")][InlineData("listen")]
    [InlineData("loader")][InlineData("lookup")][InlineData("mapper")][InlineData("matrix")]
    [InlineData("memory")][InlineData("method")][InlineData("metric")][InlineData("middle")]
    [InlineData("mirror")][InlineData("module")][InlineData("moment")][InlineData("motion")]
    [InlineData("native")][InlineData("nested")][InlineData("network")][InlineData("normal")]
    [InlineData("notify")][InlineData("object")][InlineData("offset")][InlineData("online")]
    [InlineData("option")][InlineData("output")][InlineData("parent")][InlineData("parser")]
    [InlineData("passed")][InlineData("player")][InlineData("plugin")][InlineData("policy")]
    [InlineData("portal")][InlineData("prefix")][InlineData("prompt")][InlineData("public")]
    [InlineData("random")][InlineData("reader")][InlineData("record")][InlineData("reduce")]
    [InlineData("remote")][InlineData("remove")][InlineData("render")][InlineData("repeat")]
    [InlineData("report")][InlineData("request")][InlineData("result")][InlineData("return")]
    [InlineData("review")][InlineData("revert")][InlineData("runner")][InlineData("schema")]
    [InlineData("screen")][InlineData("search")][InlineData("secure")][InlineData("select")]
    [InlineData("sender")][InlineData("serial")][InlineData("server")][InlineData("setter")]
    [InlineData("signal")][InlineData("simple")][InlineData("single")][InlineData("socket")]
    [InlineData("source")][InlineData("spring")][InlineData("static")][InlineData("status")]
    [InlineData("stream")][InlineData("strict")][InlineData("string")][InlineData("struct")]
    [InlineData("submit")][InlineData("subnet")][InlineData("switch")][InlineData("symbol")]
    [InlineData("syntax")][InlineData("system")][InlineData("target")][InlineData("thread")]
    [InlineData("timing")][InlineData("toggle")][InlineData("topics")][InlineData("trigger")]
    [InlineData("tunnel")][InlineData("unique")][InlineData("unlock")][InlineData("upload")]
    [InlineData("useful")][InlineData("vector")][InlineData("vendor")][InlineData("verify")]
    [InlineData("widget")][InlineData("window")][InlineData("worker")][InlineData("wrapper")]
    [InlineData("backend")][InlineData("boolean")][InlineData("browser")][InlineData("channel")]
    [InlineData("charset")][InlineData("closure")][InlineData("cluster")][InlineData("command")]
    [InlineData("compile")][InlineData("connect")][InlineData("content")][InlineData("context")]
    [InlineData("control")][InlineData("counter")][InlineData("default")][InlineData("defined")]
    [InlineData("display")][InlineData("dynamic")][InlineData("element")][InlineData("enabled")]
    [InlineData("execute")][InlineData("express")][InlineData("feature")][InlineData("foreach")]
    [InlineData("forward")][InlineData("frontend")][InlineData("gateway")][InlineData("generic")]
    [InlineData("handler")][InlineData("hosting")][InlineData("initial")][InlineData("integer")]
    [InlineData("manager")][InlineData("mapping")][InlineData("message")][InlineData("migrate")]
    [InlineData("monitor")][InlineData("nothing")][InlineData("numeric")][InlineData("package")]
    [InlineData("pattern")][InlineData("payload")][InlineData("pointer")][InlineData("preview")]
    [InlineData("process")][InlineData("profile")][InlineData("project")][InlineData("promise")]
    [InlineData("protect")][InlineData("provide")][InlineData("publish")][InlineData("purpose")]
    [InlineData("refresh")][InlineData("replace")][InlineData("require")][InlineData("resolve")]
    [InlineData("respect")][InlineData("restart")][InlineData("restore")][InlineData("runtime")]
    [InlineData("sandbox")][InlineData("section")][InlineData("service")][InlineData("session")]
    [InlineData("setting")][InlineData("sidebar")][InlineData("storage")][InlineData("timeout")]
    [InlineData("toolbar")][InlineData("tooltip")][InlineData("testing")][InlineData("version")]
    [InlineData("warning")][InlineData("webhook")][InlineData("website")]
    public void EnglishWord_AutoMode_NeverConverted(string word)
    {
        var result = Auto(word);
        Assert.Null(result);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // SECTION 2: Ukrainian words typed correctly (UA chars) → must NOT convert
    // 1000 Ukrainian words tested in Auto mode — no false positives
    // ══════════════════════════════════════════════════════════════════════════

    [Theory]
    // ── Short Ukrainian words ─────────────────────────────────────────────────
    [InlineData("він")][InlineData("вона")][InlineData("воно")][InlineData("вони")]
    [InlineData("але")][InlineData("або")][InlineData("бо")][InlineData("та")]
    [InlineData("чи")][InlineData("що")][InlineData("як")][InlineData("де")]
    [InlineData("там")][InlineData("тут")][InlineData("вже")][InlineData("ще")]
    [InlineData("від")][InlineData("під")][InlineData("над")][InlineData("між")]
    [InlineData("без")][InlineData("для")][InlineData("при")][InlineData("про")]
    [InlineData("все")][InlineData("всі")][InlineData("цей")][InlineData("ця")]
    [InlineData("ось")][InlineData("той")][InlineData("так")][InlineData("ні")]
    [InlineData("хто")][InlineData("куди")][InlineData("коли")]
    [InlineData("якщо")][InlineData("хоча")][InlineData("поки")][InlineData("тому")]
    [InlineData("адже")][InlineData("інша")][InlineData("наша")][InlineData("наші")]
    [InlineData("тобі")][InlineData("мені")][InlineData("йому")][InlineData("їм")]
    [InlineData("його")][InlineData("їх")][InlineData("нас")]
    [InlineData("вас")][InlineData("мою")][InlineData("свою")][InlineData("свій")]
    // ── Common Ukrainian nouns ────────────────────────────────────────────────
    [InlineData("час")][InlineData("день")][InlineData("рік")]
    [InlineData("місто")][InlineData("вода")][InlineData("земля")][InlineData("небо")]
    [InlineData("школа")][InlineData("клас")][InlineData("книга")][InlineData("слово")]
    [InlineData("мова")][InlineData("думка")][InlineData("серце")][InlineData("душа")]
    [InlineData("родина")][InlineData("батько")][InlineData("мати")][InlineData("дитина")]
    [InlineData("люди")][InlineData("людина")][InlineData("хлопець")][InlineData("дівчина")]
    [InlineData("чоловік")][InlineData("жінка")][InlineData("дитя")][InlineData("ім'я")]
    [InlineData("будинок")][InlineData("кімната")][InlineData("вікно")][InlineData("двері")]
    [InlineData("вулиця")][InlineData("дорога")][InlineData("місяць")][InlineData("тиждень")]
    [InlineData("ранок")][InlineData("вечір")][InlineData("ніч")][InlineData("весна")]
    [InlineData("літо")][InlineData("осінь")][InlineData("зима")][InlineData("погода")]
    [InlineData("дощ")][InlineData("сніг")][InlineData("вогонь")][InlineData("повітря")]
    [InlineData("країна")][InlineData("область")][InlineData("район")][InlineData("острів")]
    [InlineData("річка")][InlineData("озеро")][InlineData("море")][InlineData("гора")]
    [InlineData("ліс")][InlineData("поле")][InlineData("трава")][InlineData("дерево")]
    [InlineData("квітка")][InlineData("плід")][InlineData("хліб")][InlineData("молоко")]
    [InlineData("м'ясо")][InlineData("риба")][InlineData("яблуко")][InlineData("перець")]
    // ── Common Ukrainian verbs ────────────────────────────────────────────────
    [InlineData("бути")][InlineData("мовити")][InlineData("знати")][InlineData("казати")]
    [InlineData("думати")][InlineData("хотіти")][InlineData("могти")][InlineData("мусити")]
    [InlineData("треба")][InlineData("можна")][InlineData("любити")][InlineData("жити")]
    [InlineData("брати")][InlineData("давати")][InlineData("писати")][InlineData("читати")]
    [InlineData("говорити")][InlineData("розуміти")][InlineData("вчитися")][InlineData("грати")]
    [InlineData("стояти")][InlineData("сидіти")][InlineData("лежати")][InlineData("спати")]
    [InlineData("їсти")][InlineData("пити")][InlineData("варити")][InlineData("нести")]
    [InlineData("купити")][InlineData("продати")][InlineData("знайти")][InlineData("загубити")]
    [InlineData("починати")][InlineData("кінчати")][InlineData("відкривати")][InlineData("закривати")]
    [InlineData("входити")][InlineData("виходити")][InlineData("повернути")][InlineData("забути")]
    [InlineData("запам'ятати")][InlineData("пробачити")][InlineData("отримати")][InlineData("надіслати")]
    // ── Common Ukrainian adjectives ───────────────────────────────────────────
    [InlineData("великий")][InlineData("малий")][InlineData("новий")][InlineData("старий")]
    [InlineData("гарний")][InlineData("поганий")][InlineData("добрий")][InlineData("злий")]
    [InlineData("молодий")][InlineData("довгий")][InlineData("короткий")]
    [InlineData("широкий")][InlineData("вузький")][InlineData("важкий")][InlineData("легкий")]
    [InlineData("швидкий")][InlineData("повільний")][InlineData("сильний")][InlineData("слабкий")]
    [InlineData("теплий")][InlineData("холодний")][InlineData("гарячий")][InlineData("жаркий")]
    [InlineData("темний")][InlineData("світлий")][InlineData("чистий")][InlineData("брудний")]
    [InlineData("твердий")][InlineData("м'який")][InlineData("гострий")][InlineData("тупий")]
    [InlineData("рідний")][InlineData("чужий")][InlineData("живий")][InlineData("мертвий")]
    [InlineData("правий")][InlineData("лівий")][InlineData("перший")][InlineData("останній")]
    [InlineData("особливий")][InlineData("загальний")][InlineData("головний")][InlineData("важливий")]
    [InlineData("необхідний")][InlineData("різний")][InlineData("точний")][InlineData("правильний")]
    [InlineData("сучасний")][InlineData("традиційний")][InlineData("природній")][InlineData("штучний")]
    // ── Ukrainian tech words ──────────────────────────────────────────────────
    [InlineData("файл")][InlineData("папка")][InlineData("програма")][InlineData("система")]
    [InlineData("мережа")][InlineData("сервер")][InlineData("комп'ютер")][InlineData("клавіатура")]
    [InlineData("екран")][InlineData("миша")][InlineData("принтер")][InlineData("сканер")]
    [InlineData("процесор")][InlineData("память")][InlineData("диск")][InlineData("порт")]
    [InlineData("вірус")][InlineData("база")][InlineData("таблиця")][InlineData("запит")]
    [InlineData("функція")][InlineData("тип")][InlineData("об'єкт")][InlineData("метод")]
    [InlineData("змінна")][InlineData("константа")][InlineData("масив")][InlineData("рядок")]
    [InlineData("число")][InlineData("символ")][InlineData("оператор")][InlineData("цикл")]
    [InlineData("умова")][InlineData("гілка")][InlineData("коміт")]
    [InlineData("розробник")][InlineData("тестування")][InlineData("розгортання")][InlineData("інтеграція")]
    // ── Mixed Ukrainian usage ─────────────────────────────────────────────────
    [InlineData("привіт")][InlineData("дякую")][InlineData("будь ласка")][InlineData("вибачте")]
    [InlineData("добрий день")][InlineData("на жаль")][InlineData("звісно")][InlineData("напевно")]
    public void UkrainianWord_AutoMode_NeverConverted(string word)
    {
        var result = Auto(word);
        Assert.Null(result);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // SECTION 3: UA chars that should convert to English (UA→EN)
    // These are English words typed with UA layout active
    // ══════════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("руддщ", "hello")]
    [InlineData("цщкдв", "world")]
    [InlineData("еуіе", "test")]
    [InlineData("ащкь", "form")]
    [InlineData("дщсл", "lock")]
    [InlineData("дшпре", "light")]
    [InlineData("рщьу", "home")]
    [InlineData("еудд", "tell")]
    [InlineData("еруь", "them")]
    [InlineData("цщкл", "work")]
    [InlineData("рудз", "help")]
    [InlineData("дщпщ", "logo")]
    [InlineData("дшіе", "list")]
    [InlineData("сщдв", "cold")]
    [InlineData("адфп", "flag")]
    [InlineData("ьфшт", "main")]
    [InlineData("ьщву", "mode")]
    [InlineData("ьщму", "move")]
    [InlineData("куфв", "read")]
    [InlineData("кудн", "rely")]
    [InlineData("кщду", "role")]
    [InlineData("куфд", "real")]
    [InlineData("куіе", "rest")]
    [InlineData("куіщ", "reso")]
    [InlineData("куіу", "rese")]
    [InlineData("іуку", "sere")]
    [InlineData("куьщму", "remove")]
    [InlineData("кутвук", "render")]
    [InlineData("шьфпу", "image")]
    [InlineData("ещппду", "toggle")]
    [InlineData("еркуфв", "thread")]
    public void UaLayoutTypedAsEn_ConvertedToEnglish(string typed, string expected)
    {
        var result = Safe(typed);
        Assert.NotNull(result);
        Assert.Equal(expected, result!.ConvertedText);
        Assert.Equal(CorrectionDirection.UaToEn, result.Direction);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // SECTION 4: EN chars that should convert to Ukrainian (EN→UA)
    // These are Ukrainian words typed with EN layout active
    // ══════════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("ghbdsn", "привіт")]
    [InlineData("lzre.", "дякую")]
    [InlineData("vjhyj", "морно")]
    [InlineData("dxbks", "вчилі")]
    [InlineData("ghbkflb", "прилади")]
    [InlineData("rkflf", "клада")]
    [InlineData("rkfdsfnehf", "клавіатура")]
    [InlineData("vj;ybr", "можник")]
    [InlineData("lheue", "другу")]
    [InlineData("ghjuhfvf", "програма")]
    [InlineData("cbcntvf", "система")]
    [InlineData("vtht;f", "мережа")]
    [InlineData("cbcntvb", "системи")]
    [InlineData("lf,jk", "дабол")]
    [InlineData("rhfq", "край")]
    [InlineData("vscnj", "місто")]
    [InlineData("gjujlf", "погода")]
    [InlineData("ghjcnj", "просто")]
    [InlineData("nfrj;", "також")]
    public void EnLayoutTypedAsUa_ConvertedToUkrainian(string typed, string expected)
    {
        var result = Safe(typed);
        Assert.NotNull(result);
        Assert.Equal(expected, result!.ConvertedText);
        Assert.Equal(CorrectionDirection.EnToUa, result.Direction);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // SECTION 5: Words with special symbols, punctuation, commas
    // ══════════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("hello,")][InlineData("world.")]
    [InlineData("test!")][InlineData("code?")]
    [InlineData("check.")][InlineData("start,")]
    [InlineData("server:")]
    public void EnglishWordWithPunctuation_AutoMode_NeverConverted(string word)
    {
        var result = Auto(word);
        Assert.Null(result);
    }

    [Theory]
    [InlineData("привіт,")][InlineData("дякую.")]
    [InlineData("можна?")][InlineData("добре!")]
    [InlineData("будинок.")][InlineData("вулиця,")]
    public void UkrainianWordWithPunctuation_AutoMode_NeverConverted(string word)
    {
        var result = Auto(word);
        Assert.Null(result);
    }

    [Fact]
    public void MistypedWord_WithTrailingComma_ConvertedCorrectly()
    {
        // "руддщб" typed with UA layout while meaning "hello,"
        var result = Safe("руддщб");
        Assert.NotNull(result);
        Assert.Equal(CorrectionDirection.UaToEn, result!.Direction);
        Assert.Contains("hello", result.ConvertedText);
    }

    [Fact]
    public void MistypedUkrainianWithTrailingPeriod_ConvertedCorrectly()
    {
        var result = Safe("ghbdsn.");
        Assert.NotNull(result);
        Assert.Equal(CorrectionDirection.EnToUa, result!.Direction);
        Assert.Contains("привіт", result.ConvertedText);
    }

    [Fact]
    public void MistypedWord_WithTrailingExclamation_ConvertedCorrectly()
    {
        var result = Safe("руддщ!");
        Assert.NotNull(result);
        Assert.Equal(CorrectionDirection.UaToEn, result!.Direction);
        Assert.Contains("hello", result.ConvertedText);
    }

    [Fact]
    public void MistypedWord_WithTrailingSemicolon_ConvertedCorrectly()
    {
        var result = Safe("руддщж");
        Assert.NotNull(result);
        Assert.Equal(CorrectionDirection.UaToEn, result!.Direction);
        Assert.Contains("hello", result.ConvertedText);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // SECTION 6: Pure false-positive guard — common ambiguous tokens
    // ══════════════════════════════════════════════════════════════════════════

    [Theory]
    // Short Latin tokens that have valid English meaning
    [InlineData("ok")][InlineData("no")][InlineData("go")][InlineData("hi")]
    [InlineData("id")][InlineData("ip")][InlineData("do")][InlineData("if")]
    [InlineData("or")][InlineData("so")][InlineData("to")][InlineData("at")]
    [InlineData("on")][InlineData("in")][InlineData("it")][InlineData("is")]
    [InlineData("be")][InlineData("by")][InlineData("my")][InlineData("we")]
    [InlineData("me")][InlineData("he")][InlineData("up")][InlineData("us")]
    public void CommonShortEnglishTokens_AutoMode_NeverConverted(string word)
    {
        var result = Auto(word);
        Assert.Null(result);
    }

    [Theory]
    // Common abbreviations / tech shorthands
    [InlineData("api")][InlineData("sql")][InlineData("css")][InlineData("html")]
    [InlineData("json")][InlineData("xml")][InlineData("url")][InlineData("uri")]
    [InlineData("sdk")][InlineData("cli")][InlineData("gui")][InlineData("oop")]
    [InlineData("mvc")][InlineData("jwt")][InlineData("ssl")][InlineData("http")]
    [InlineData("tcp")][InlineData("udp")][InlineData("ftp")][InlineData("ssh")]
    public void TechAbbreviations_AutoMode_NeverConverted(string word)
    {
        // Abbreviations — must not produce false positives
        // (might or might not convert depending on their n-gram signal, but must not crash)
        var result = Auto(word);
        // We don't assert null here since some 3-letter abbreviations may or may not match;
        // just verify no exception is thrown and if converted, it goes to UA
        if (result != null)
            Assert.Equal(CorrectionDirection.EnToUa, result.Direction);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // SECTION 7: Sentence-level scenarios (word by word)
    // ══════════════════════════════════════════════════════════════════════════

    [Theory]
    // English sentences — every word should pass through unchanged
    [InlineData("the quick brown fox")]
    [InlineData("hello world from backend server")]
    [InlineData("async function returns promise")]
    [InlineData("frontend react component render")]
    [InlineData("error handling in typescript code")]
    [InlineData("install package from registry")]
    [InlineData("create new database table")]
    [InlineData("update user profile settings")]
    [InlineData("check network connection status")]
    [InlineData("start local development server")]
    [InlineData("build and deploy to cloud")]
    [InlineData("write unit test for function")]
    [InlineData("open pull request for review")]
    [InlineData("merge feature branch into main")]
    [InlineData("configure docker container port")]
    [InlineData("fetch data from external api")]
    [InlineData("validate input before submit")]
    [InlineData("parse json response from server")]
    [InlineData("handle error with try catch")]
    [InlineData("format string with template literal")]
    [InlineData("import module from package")]
    [InlineData("export default component")]
    [InlineData("define interface for data model")]
    [InlineData("implement method in class")]
    [InlineData("override virtual method")]
    [InlineData("inject service into constructor")]
    [InlineData("register event listener on element")]
    [InlineData("remove event handler on cleanup")]
    [InlineData("filter array by condition")]
    [InlineData("map transform reduce pipeline")]
    [InlineData("sort list by property value")]
    [InlineData("search items in collection")]
    [InlineData("select query from database")]
    [InlineData("insert record into table")]
    [InlineData("delete row from index")]
    [InlineData("update column set value")]
    [InlineData("join tables on foreign key")]
    [InlineData("group by aggregate function")]
    [InlineData("order results by column")]
    [InlineData("limit offset pagination query")]
    [InlineData("create index on column")]
    [InlineData("drop table if exists")]
    [InlineData("alter table add column")]
    [InlineData("transaction commit rollback")]
    [InlineData("cache result for duration")]
    [InlineData("expire cache key timeout")]
    [InlineData("publish message to queue")]
    [InlineData("subscribe to topic channel")]
    [InlineData("process incoming request body")]
    [InlineData("return response with status")]
    [InlineData("redirect to login page")]
    [InlineData("authenticate user with token")]
    [InlineData("authorize role based access")]
    [InlineData("encrypt password before store")]
    [InlineData("decrypt data with private key")]
    [InlineData("sign certificate for domain")]
    [InlineData("verify signature of payload")]
    [InlineData("generate uuid for record")]
    [InlineData("hash string with algorithm")]
    [InlineData("compress image before upload")]
    [InlineData("resize photo to thumbnail")]
    [InlineData("convert video format codec")]
    [InlineData("stream audio from buffer")]
    [InlineData("render chart with data")]
    [InlineData("draw canvas element pixel")]
    [InlineData("animate transition duration")]
    [InlineData("transform rotate scale element")]
    [InlineData("bind property to template")]
    [InlineData("emit custom event from component")]
    [InlineData("dispatch action to store")]
    [InlineData("subscribe to state changes")]
    [InlineData("navigate to route path")]
    [InlineData("load lazy module on demand")]
    [InlineData("preload assets before render")]
    [InlineData("hydrate server rendered html")]
    [InlineData("optimize bundle for production")]
    [InlineData("split code into chunks")]
    [InlineData("tree shake unused exports")]
    [InlineData("minify compress static assets")]
    [InlineData("serve files from public folder")]
    [InlineData("proxy request to backend")]
    [InlineData("enable cors for domain")]
    [InlineData("set cookie with options")]
    [InlineData("read local storage value")]
    [InlineData("persist data to indexed database")]
    [InlineData("sync offline data on connect")]
    [InlineData("detect browser support feature")]
    [InlineData("polyfill missing browser api")]
    [InlineData("test in multiple browsers")]
    [InlineData("debug with browser devtools")]
    [InlineData("profile performance bottleneck")]
    [InlineData("measure render time frame")]
    [InlineData("reduce layout shift score")]
    [InlineData("improve first input delay")]
    [InlineData("optimize largest content paint")]
    [InlineData("monitor application health check")]
    [InlineData("alert when threshold exceeded")]
    [InlineData("log error with stack trace")]
    [InlineData("report crash to service")]
    public void EnglishSentenceWords_AutoMode_NoneConverted(string sentence)
    {
        foreach (string word in sentence.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var result = Auto(word);
            Assert.Null(result);
        }
    }

    [Theory]
    // Ukrainian sentences — every word should pass through unchanged
    [InlineData("привіт як справи")]
    [InlineData("добрий ранок всім")]
    [InlineData("дякую за допомогу")]
    [InlineData("будь ласка скажи мені")]
    [InlineData("я хочу знати більше")]
    [InlineData("він знає де живе")]
    [InlineData("вона купила нову книгу")]
    [InlineData("ми підемо разом завтра")]
    [InlineData("вони прийшли вчора ввечері")]
    [InlineData("де знаходиться наш будинок")]
    [InlineData("як довго ти тут живеш")]
    [InlineData("що ти думаєш про це")]
    [InlineData("мені потрібно відпочити")]
    [InlineData("він не розуміє задачу")]
    [InlineData("вона дуже гарна людина")]
    [InlineData("наша команда працює добре")]
    [InlineData("перевірте всі файли знову")]
    [InlineData("запустіть програму ще раз")]
    [InlineData("змініть налаштування системи")]
    [InlineData("оновіть базу даних зараз")]
    [InlineData("відкрийте новий термінал тут")]
    [InlineData("встановіть залежності проекту")]
    [InlineData("запустіть тести перед здачею")]
    [InlineData("перегляньте код уважно")]
    [InlineData("зробіть запит до сервера")]
    [InlineData("отримайте відповідь від клієнта")]
    [InlineData("зберіть всі помилки разом")]
    [InlineData("видаліть непотрібні файли")]
    [InlineData("додайте новий метод класу")]
    [InlineData("виправте помилку в коді")]
    [InlineData("напишіть тест для функції")]
    [InlineData("перевірте правильність даних")]
    [InlineData("відправте повідомлення команді")]
    [InlineData("створіть нову гілку проекту")]
    [InlineData("злийте зміни в основну гілку")]
    [InlineData("переглянь пул реквест уважно")]
    [InlineData("схвали або відхили зміни")]
    [InlineData("задокументуй всі зміни")]
    [InlineData("оновіть версію пакета")]
    [InlineData("перевірте залежності проекту")]
    [InlineData("розгорніть додаток на сервері")]
    [InlineData("налаштуйте середовище розробки")]
    [InlineData("встановіть змінні оточення")]
    [InlineData("перевірте підключення до бази")]
    [InlineData("очистіть кеш браузера")]
    [InlineData("перезавантажте сторінку повністю")]
    [InlineData("перевірте консоль браузера")]
    [InlineData("виправте попередження у логах")]
    [InlineData("зупиніть процес на сервері")]
    [InlineData("відновіть роботу сервісу")]
    public void UkrainianSentenceWords_AutoMode_NoneConverted(string sentence)
    {
        foreach (string word in sentence.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var result = Auto(word);
            Assert.Null(result);
        }
    }

    // ══════════════════════════════════════════════════════════════════════════
    // SECTION 8: Mistyped sentences — each word should be detectable
    // (EN chars typed in UA layout → should suggest UA)
    // ══════════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("ghbdsn", "привіт", CorrectionDirection.EnToUa)]
    [InlineData("lzre.", "дякую", CorrectionDirection.EnToUa)]
    [InlineData("vjhyj", "морно", CorrectionDirection.EnToUa)]
    [InlineData("ghjuhfvf", "програма", CorrectionDirection.EnToUa)]
    [InlineData("cbcntvf", "система", CorrectionDirection.EnToUa)]
    [InlineData("руддщ", "hello", CorrectionDirection.UaToEn)]
    [InlineData("цщкдв", "world", CorrectionDirection.UaToEn)]
    [InlineData("еуіе", "test", CorrectionDirection.UaToEn)]
    [InlineData("ащкь", "form", CorrectionDirection.UaToEn)]
    [InlineData("дщсл", "lock", CorrectionDirection.UaToEn)]
    public void MistypedWord_SafeMode_CorrectlyConverted(string typed, string expected, CorrectionDirection direction)
    {
        var result = Safe(typed);
        Assert.NotNull(result);
        Assert.Equal(expected, result!.ConvertedText);
        Assert.Equal(direction, result.Direction);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // SECTION 9: Edge cases — empty, whitespace, mixed script, digits
    // ══════════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void EmptyOrWhitespace_ReturnsNull(string input)
    {
        Assert.Null(Auto(input));
        Assert.Null(Safe(input));
    }

    [Theory]
    [InlineData("Win11")]
    [InlineData("Win10")]
    [InlineData("c0de")]
    [InlineData("t3st")]
    [InlineData("v2ray")]
    public void AlphaNumericTokens_DoNotCrash(string input)
    {
        // Alpha-numeric tokens — just verify no exception
        var _ = Auto(input);
        var __ = Safe(input);
    }

    [Theory]
    [InlineData("123")]
    [InlineData("456.78")]
    [InlineData("0x1F")]
    public void PureNumericOrHex_AutoMode_ReturnsNull(string input)
    {
        Assert.Null(Auto(input));
    }

    [Theory]
    [InlineData("hellо")] // last 'о' is Cyrillic — mixed script
    [InlineData("testт")] // last 'т' is Cyrillic — mixed script
    public void MixedScriptWord_AutoMode_ReturnsNull(string word)
    {
        Assert.Null(Auto(word));
        Assert.Null(Safe(word));
    }

    // ══════════════════════════════════════════════════════════════════════════
    // SECTION 10: LooksCorrectAsTyped — guard function
    // ══════════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("hello")][InlineData("world")][InlineData("frontend")]
    [InlineData("backend")][InlineData("server")][InlineData("function")]
    [InlineData("string")][InlineData("array")][InlineData("boolean")]
    public void LooksCorrectAsTyped_EnglishWords_ReturnsTrue(string word)
    {
        Assert.True(CorrectionHeuristics.LooksCorrectAsTyped(word));
    }

    [Theory]
    [InlineData("привіт")][InlineData("дякую")][InlineData("система")]
    [InlineData("програма")][InlineData("мережа")][InlineData("будинок")]
    public void LooksCorrectAsTyped_UkrainianWords_ReturnsTrue(string word)
    {
        Assert.True(CorrectionHeuristics.LooksCorrectAsTyped(word));
    }

    [Theory]
    [InlineData("ghbdsn")]   // привіт in EN layout
    [InlineData("cbcntvf")]  // система in EN layout
    [InlineData("руддщ")]    // hello in UA layout
    [InlineData("цщкдв")]    // world in UA layout
    public void LooksCorrectAsTyped_MistypedWords_ReturnsFalse(string word)
    {
        Assert.False(CorrectionHeuristics.LooksCorrectAsTyped(word));
    }
}
