using System.Globalization;

namespace AlienHelper;

/// <summary>UI strings. Order of translations: ru, en, fr, es, ja, zh.</summary>
public static class L
{
    public static readonly (string code, string name)[] Languages =
    {
        ("ru", "Русский"), ("en", "English"), ("fr", "Français"), ("es", "Español"), ("ja", "日本語"), ("zh", "中文"),
    };

    static int _index = 1;
    public static string Current => Languages[_index].code;

    /// <summary>Picks the saved language, else the Windows culture, else English.</summary>
    public static void Init(string? saved)
    {
        string? pick = saved;
        if (string.IsNullOrEmpty(pick))
        {
            foreach (var ci in new[] { CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture })
                if (Array.FindIndex(Languages, l => l.code == ci.TwoLetterISOLanguageName) >= 0) { pick = ci.TwoLetterISOLanguageName; break; }
        }
        Set(pick ?? "en");
    }

    public static void Set(string code)
    {
        int i = Array.FindIndex(Languages, l => l.code == code);
        _index = i >= 0 ? i : 1;
        // number formatting (decimal separator, grouping) follows the UI language
        var ci = CultureInfo.GetCultureInfo(Current);
        CultureInfo.CurrentCulture = ci;
        CultureInfo.DefaultThreadCurrentCulture = ci;
    }

    public static string T(string key) => Strings.TryGetValue(key, out var a) ? a[_index] : key;
    public static string T(string key, params object[] args) => string.Format(T(key), args);

    static readonly Dictionary<string, string[]> Strings = new()
    {
        // main window
        ["btn.overlay"] = new[] { "Оверлей", "Overlay", "Overlay", "Superposición", "オーバーレイ", "叠加层" },
        ["btn.charts"] = new[] { "Графики", "Charts", "Graphiques", "Gráficos", "グラフ", "图表" },
        ["card.monitor"] = new[] { "Мониторинг", "Monitoring", "Surveillance", "Monitorización", "モニター", "监控" },
        ["card.cards"] = new[] { "Главное окно", "Main window", "Fenêtre principale", "Ventana principal", "メイン ウィンドウ", "主窗口" },
        ["card.cards.hint"] = new[] { "Какие разделы показывать. Хотя бы один остаётся включённым.", "Sections to show. At least one stays on.", "Sections à afficher. Au moins une reste activée.", "Secciones que se muestran. Al menos una queda activa.", "表示するセクション。少なくとも 1 つはオンのままです。", "要显示的部分。至少保留一个。" },
        ["boost.custom.tip"] = new[]
        {
            "Свои кривые: буст вентиляторов CPU и GPU плавно растёт с их температурой. Ползунки при этом заблокированы.",
            "Custom curves: CPU and GPU fan boost rises smoothly with their temperature. The sliders are locked meanwhile.",
            "Courbes perso : le boost des ventilateurs CPU et GPU suit leur température. Les curseurs sont verrouillés.",
            "Curvas propias: el impulso de los ventiladores de CPU y GPU sube con su temperatura. Los controles quedan bloqueados.",
            "カスタム カーブ: CPU と GPU のファン ブーストが温度に合わせて滑らかに上がります。スライダーはロックされます。",
            "自定义曲线：CPU 和 GPU 风扇增强随温度平滑上升，滑块此时锁定。",
        },
        ["curve.quiet"] = new[] { "Тихий", "Quiet", "Silencieux", "Silencioso", "静音", "安静" },
        ["curve.balanced"] = new[] { "Сбалансированный", "Balanced", "Équilibré", "Equilibrado", "バランス", "均衡" },
        ["curve.performance"] = new[] { "Производительный", "Performance", "Performance", "Rendimiento", "パフォーマンス", "性能" },
        ["curve.max"] = new[] { "Максимальное охлаждение", "Maximum cooling", "Refroidissement max", "Refrigeración máxima", "最大冷却", "最大散热" },
        ["curves.profile"] = new[] { "Профиль", "Profile", "Profil", "Perfil", "プロファイル", "配置" },
        ["curves.presets"] = new[] { "Готовые", "Built-in", "Prédéfinis", "Predefinidos", "プリセット", "预设" },
        ["curves.mine"] = new[] { "Мои", "Mine", "Les miens", "Míos", "マイ", "我的" },
        ["curves.newname"] = new[] { "Мой профиль {0}", "My profile {0}", "Mon profil {0}", "Mi perfil {0}", "マイ プロファイル {0}", "我的配置 {0}" },
        ["curves.addhint"] = new[] { "Двойной щелчок — добавить точку, правый — удалить", "Double-click adds a point, right-click removes one", "Double-clic : ajouter un point, clic droit : supprimer", "Doble clic añade un punto, clic derecho lo quita", "ダブルクリックで点を追加、右クリックで削除", "双击添加点，右键删除" },
        ["curves.basetip"] = new[]
        {
            "Задаёт лимиты мощности процессора и видеокарты (производительность). Вентиляторы в Custom всегда идут по кривым.",
            "Sets the CPU and GPU power limits (performance). In Custom the fans always follow the curves.",
            "Fixe les limites de puissance du processeur et de la carte graphique (performances). En Custom, les ventilateurs suivent toujours les courbes.",
            "Fija los límites de potencia del procesador y la tarjeta gráfica (rendimiento). En Custom los ventiladores siempre siguen las curvas.",
            "CPU と GPU の電力制限 (性能) を決めます。Custom ではファンは常にカーブに従います。",
            "设定处理器和显卡的功耗上限 (性能)。在 Custom 中风扇始终按曲线运行。",
        },
        ["curves.base"] = new[] { "Режим прошивки под Custom", "Firmware mode under Custom", "Mode firmware sous Custom", "Modo de firmware bajo Custom", "Custom の基本モード", "Custom 的固件模式" },
        ["oc.title"] = new[] { "Разгон GPU", "GPU overclock", "Overclock GPU", "Overclock GPU", "GPU OC", "GPU 超频" },
        ["oc.mild"] = new[] { "Мягкий", "Mild", "Léger", "Suave", "控えめ", "轻度" },
        ["oc.medium"] = new[] { "Средний", "Medium", "Moyen", "Medio", "中", "中度" },
        ["oc.strong"] = new[] { "Сильный", "Strong", "Fort", "Fuerte", "強", "强" },
        ["oc.add"] = new[] { "ядро +{0} МГц, память +{1} МГц к прежним смещениям", "core +{0} MHz, memory +{1} MHz over the previous offsets", "cœur +{0} MHz, mémoire +{1} MHz en plus", "núcleo +{0} MHz, memoria +{1} MHz sobre los anteriores", "コア +{0} MHz、メモリ +{1} MHz（既存の値に加算）", "核心 +{0} MHz、显存 +{1} MHz（在原偏移之上）" },
        ["oc.state"] = new[] { "GPU: ядро {0} МГц, память {1} МГц", "GPU: core {0} MHz, memory {1} MHz", "GPU : cœur {0} MHz, mémoire {1} MHz", "GPU: núcleo {0} MHz, memoria {1} MHz", "GPU: コア {0} MHz、メモリ {1} MHz", "GPU：核心 {0} MHz，显存 {1} MHz" },
        ["oc.refused"] = new[] { "драйвер отклонил разгон", "the driver refused the overclock", "le pilote a refusé", "el controlador lo rechazó", "ドライバーが拒否しました", "驱动拒绝了超频" },
        ["oc.tip"] = new[]
        {
            "Разгон видеокарты в Custom ({0}). ⚠ Возможны сбои. Клавиши: {1}",
            "GPU overclock in Custom ({0}). ⚠ May be unstable. Hotkey: {1}",
            "Overclock GPU en Custom ({0}). ⚠ Peut être instable. Raccourci : {1}",
            "Overclock de GPU en Custom ({0}). ⚠ Puede ser inestable. Atajo: {1}",
            "Custom での GPU OC（{0}）。⚠ 不安定になる場合あり。キー: {1}",
            "Custom 下的 GPU 超频（{0}）。⚠ 可能不稳定。快捷键：{1}",
        },
        ["oc.warn"] = new[]
        {
            "Разгон видеокарты повышает частоты сверх заводских. Возможны вылеты игр, артефакты на экране или сбой драйвера.\n\nЕсли что-то пошло не так, выключите Overclock — прежние частоты вернутся сразу (и при выходе из программы).\n\nВключить разгон?",
            "Overclocking raises the GPU clocks above stock. Games may crash, the screen may show artifacts or the driver may reset.\n\nIf anything goes wrong, switch Overclock off: the previous clocks come back at once (and when the app exits).\n\nTurn the overclock on?",
            "L'overclock augmente les fréquences du GPU. Plantages de jeux, artefacts ou réinitialisation du pilote sont possibles.\n\nEn cas de problème, désactivez Overclock : les fréquences précédentes reviennent aussitôt.\n\nActiver l'overclock ?",
            "El overclock sube las frecuencias de la GPU. Pueden cerrarse juegos, aparecer artefactos o reiniciarse el controlador.\n\nSi algo falla, desactiva Overclock: vuelven las frecuencias anteriores al instante.\n\n¿Activar el overclock?",
            "オーバークロックは GPU のクロックを定格以上に上げます。ゲームのクラッシュ、表示の乱れ、ドライバーのリセットが起きる場合があります。\n\n問題があれば Overclock をオフにすると、すぐ元のクロックに戻ります。\n\n有効にしますか？",
            "超频会把 GPU 频率提高到出厂值以上，可能导致游戏崩溃、画面花屏或驱动重置。\n\n如有问题，关闭 Overclock 即可立即恢复原频率。\n\n要开启超频吗？",
        },
        ["oc.onlycustom"] = new[] { "Overclock — только в Custom", "Overclock works in Custom only", "Overclock : Custom uniquement", "Overclock: solo en Custom", "Overclock は Custom のみ", "Overclock 仅限 Custom" },
        ["oc.confirmfirst"] = new[] { "Overclock: первый раз включите в окне", "Overclock: turn it on in the window the first time", "Overclock : activez-le d'abord dans la fenêtre", "Overclock: actívalo primero en la ventana", "Overclock: 初回はウィンドウで有効化", "Overclock：首次请在窗口中开启" },
        ["oc.on"] = new[] { "вкл", "on", "activé", "activado", "オン", "开" },
        ["oc.off"] = new[] { "выкл", "off", "désactivé", "desactivado", "オフ", "关" },
        ["hk.oc"] = new[] { "Overclock: вкл / выкл", "Overclock: on / off", "Overclock : on / off", "Overclock: sí / no", "Overclock オン/オフ", "Overclock 开/关" },
        ["curves.btn"] = new[] { "Кривые…", "Curves…", "Courbes…", "Curvas…", "カーブ…", "曲线…" },
        ["curves.title"] = new[] { "Кривые вентиляторов", "Fan curves", "Courbes des ventilateurs", "Curvas de ventiladores", "ファン カーブ", "风扇曲线" },
        ["curves.reset"] = new[] { "Сбросить", "Reset", "Réinitialiser", "Restablecer", "リセット", "重置" },
        ["curves.done"] = new[] { "Готово", "Done", "Terminé", "Listo", "完了", "完成" },
        ["curves.hint"] = new[]
        {
            "Перетаскивайте точки. По горизонтали — температура, по вертикали — буст к кривой BIOS (0 = авто). Работает при включённом Custom; в Alien Mode вентиляторы всегда на 100%.",
            "Drag the points. Across: temperature; up: boost over the BIOS curve (0 = auto). Active while Custom is on; in Alien Mode the fans always run at 100%.",
            "Faites glisser les points. Horizontal : température ; vertical : boost au-dessus de la courbe BIOS (0 = auto). Actif avec Custom ; en Alien Mode, 100 %.",
            "Arrastra los puntos. Horizontal: temperatura; vertical: impulso sobre la curva de la BIOS (0 = auto). Activo con Custom; en Alien Mode, siempre 100 %.",
            "点をドラッグします。横は温度、縦は BIOS カーブへのブースト（0 = 自動）。Custom がオンのとき有効。Alien Mode では常に 100%。",
            "拖动各点。横轴为温度，纵轴为在 BIOS 曲线之上的增强（0 = 自动）。Custom 开启时生效；Alien Mode 下风扇始终 100%。",
        },
        ["curves.now"] = new[] { "сейчас {0}° → {1}", "now {0}° → {1}", "actuel {0}° → {1}", "ahora {0}° → {1}", "現在 {0}° → {1}", "当前 {0}° → {1}" },
        ["curves.off"] = new[] { "Custom выключен — кривые не действуют", "Custom is off: the curves are not applied", "Custom désactivé : courbes inactives", "Custom desactivado: las curvas no se aplican", "Custom はオフ: カーブは無効", "Custom 已关闭：曲线未生效" },
        ["ov.mode"] = new[] { "Показ", "Show", "Affichage", "Mostrar", "表示", "显示" },
        ["ov.mode.off"] = new[] { "Выкл", "Off", "Désactivé", "Apagado", "オフ", "关闭" },
        ["ov.mode.on"] = new[] { "Всегда", "Always", "Toujours", "Siempre", "常に", "始终" },
        ["plugins.title"] = new[] { "Плагины", "Plugins", "Plugins", "Complementos", "プラグイン", "插件" },
        ["plugins.none"] = new[] { "Плагины не установлены.", "No plugins installed.", "Aucun plugin installé.", "No hay complementos instalados.", "プラグインはインストールされていません。", "未安装插件。" },
        ["plugins.hint"] = new[] { "Плагины — отдельные продукты, они работают внутри программы с её правами. Поэтому загружаются только из этой папки и только если менять её может лишь администратор:", "Plugins are separate products that run inside the app with its rights. So they load only from this folder, and only when administrators alone can change it:", "Les plugins sont des produits séparés qui s’exécutent dans l’application avec ses droits. Ils ne se chargent donc que depuis ce dossier, et seulement si seuls les administrateurs peuvent le modifier :", "Los complementos son productos aparte que se ejecutan dentro de la aplicación con sus permisos. Por eso solo se cargan desde esta carpeta y solo si únicamente los administradores pueden cambiarla:", "プラグインは別の製品で、アプリの権限でアプリ内で動作します。そのため、このフォルダーから、管理者だけが変更できる場合にのみ読み込まれます:", "插件是独立的产品，以本程序的权限在程序内运行。因此只从此文件夹加载，且仅当只有管理员能修改该文件夹时：" },
        ["plugins.folder"] = new[] { "Папка плагинов", "Plugins folder", "Dossier des plugins", "Carpeta de complementos", "プラグインのフォルダー", "插件文件夹" },
        ["plugins.duplicate"] = new[] { "Не загружен: плагин с таким id уже есть", "Not loaded: a plugin with this id is already there", "Non chargé : un plugin avec cet id existe déjà", "No cargado: ya hay un complemento con este id", "読み込まれていません: 同じ id のプラグインが既にあります", "未加载：已有相同 id 的插件" },
        ["store.title"] = new[] { "Дополнения в Microsoft Store", "Add-ons in the Microsoft Store", "Modules du Microsoft Store", "Complementos de Microsoft Store", "Microsoft Store のアドオン", "Microsoft Store 中的附加产品" },
        ["store.loading"] = new[] { "Загрузка из Microsoft Store…", "Loading from the Microsoft Store…", "Chargement depuis le Microsoft Store…", "Cargando desde Microsoft Store…", "Microsoft Store から読み込み中…", "正在从 Microsoft Store 加载…" },
        ["store.none"] = new[] { "Дополнений пока нет.", "No add-ons yet.", "Aucun module pour l’instant.", "Todavía no hay complementos.", "アドオンはまだありません。", "暂无附加产品。" },
        ["store.buy"] = new[] { "Купить · {0}", "Buy · {0}", "Acheter · {0}", "Comprar · {0}", "購入 · {0}", "购买 · {0}" },
        ["store.install"] = new[] { "Установить", "Install", "Installer", "Instalar", "インストール", "安装" },
        ["store.installed"] = new[] { "Установлено", "Installed", "Installé", "Instalado", "インストール済み", "已安装" },
        ["store.wait"] = new[] { "Покупка и установка…", "Buying and installing…", "Achat et installation…", "Comprando e instalando…", "購入とインストール中…", "正在购买并安装…" },
        ["store.error"] = new[] { "Не удалось: {0}", "Failed: {0}", "Échec : {0}", "Error: {0}", "失敗しました: {0}", "失败：{0}" },
        ["plugins.settings"] = new[] { "Настройки…", "Settings…", "Paramètres…", "Ajustes…", "設定…", "设置…" },
        ["plugins.running"] = new[] { "Работает", "Running", "En marche", "En ejecución", "実行中", "运行中" },
        ["plugins.off"] = new[] { "Выключен", "Off", "Désactivé", "Desactivado", "オフ", "已关闭" },
        ["plugins.restart"] = new[] { "Остановлен; выгрузится после перезапуска программы", "Stopped; unloads when the app restarts", "Arrêté ; déchargé au redémarrage de l’application", "Detenido; se descarga al reiniciar la aplicación", "停止しました。アプリの再起動でアンロードされます", "已停止；程序重启后卸载" },
        ["plugins.unsafe"] = new[] { "Не загружен: менять файлы плагина может не только администратор ({0})", "Not loaded: not only administrators can change the plugin’s files ({0})", "Non chargé : d’autres que les administrateurs peuvent modifier les fichiers du plugin ({0})", "No cargado: no solo los administradores pueden cambiar los archivos del complemento ({0})", "読み込まれていません: 管理者以外もプラグインのファイルを変更できます ({0})", "未加载：不仅管理员可以修改插件文件（{0}）" },
        ["plugins.api"] = new[] { "Не загружен: плагин сделан для API версии {0}, программе нужна {1}", "Not loaded: the plugin is for API version {0}, the app needs {1}", "Non chargé : le plugin vise l’API {0}, l’application attend {1}", "No cargado: el complemento es para la API {0}, la aplicación necesita {1}", "読み込まれていません: プラグインは API {0} 用で、アプリには {1} が必要です", "未加载：插件适用于 API {0}，程序需要 {1}" },
        ["plugins.nomanifest"] = new[] { "Не загружен: нет plugin.json", "Not loaded: no plugin.json", "Non chargé : pas de plugin.json", "No cargado: falta plugin.json", "読み込まれていません: plugin.json がありません", "未加载：缺少 plugin.json" },
        ["plugins.badmanifest"] = new[] { "Не загружен: в plugin.json нет id, name или assembly", "Not loaded: plugin.json lacks id, name or assembly", "Non chargé : il manque id, name ou assembly dans plugin.json", "No cargado: a plugin.json le falta id, name o assembly", "読み込まれていません: plugin.json に id、name、assembly がありません", "未加载：plugin.json 缺少 id、name 或 assembly" },
        ["plugins.noassembly"] = new[] { "Не загружен: нет файла {0}", "Not loaded: {0} is missing", "Non chargé : {0} est absent", "No cargado: falta {0}", "読み込まれていません: {0} がありません", "未加载：缺少 {0}" },
        ["plugins.notype"] = new[] { "в сборке нет класса, реализующего IPlugin", "the assembly has no class implementing IPlugin", "l’assembly ne contient aucune classe implémentant IPlugin", "el ensamblado no tiene ninguna clase que implemente IPlugin", "アセンブリに IPlugin を実装するクラスがありません", "程序集中没有实现 IPlugin 的类" },
        ["plugins.failed"] = new[] { "Ошибка, плагин остановлен: {0}", "Failed and stopped: {0}", "Erreur, plugin arrêté : {0}", "Error, complemento detenido: {0}", "エラーのため停止しました: {0}", "出错，插件已停止：{0}" },
        ["plugins.nosettings"] = new[] { "У этого плагина нет настроек.", "This plugin has no settings.", "Ce plugin n’a pas de paramètres.", "Este complemento no tiene ajustes.", "このプラグインには設定がありません。", "此插件没有设置。" },
        ["ov.app"] = new[] { "Приложение «{0}»", "App “{0}”", "Application « {0} »", "Aplicación «{0}»", "アプリ「{0}」", "应用“{0}”" },
        ["ov.app.game"] = new[] { "Считать игрой", "Count as a game", "Compter comme un jeu", "Contar como juego", "ゲームとして扱う", "视为游戏" },
        ["ov.app.notgame"] = new[] { "Не игра", "Not a game", "Pas un jeu", "No es un juego", "ゲームではない", "不是游戏" },
        ["ov.mode.games"] = new[] { "В играх", "In games", "En jeu", "En juegos", "ゲーム中", "游戏中" },
        ["card.power"] = new[] { "Питание", "Power", "Alimentation", "Energía", "電源", "电源" },
        ["pawn.core"] = new[]
        {
            "Пакет PawnIO не установлен, поэтому температуры ядер недоступны. Рекомендуем установить:",
            "The PawnIO package is not installed, so per-core temperatures are unavailable. We recommend installing it:",
            "Le paquet PawnIO n'est pas installé : les températures par cœur sont indisponibles. Nous recommandons de l'installer :",
            "El paquete PawnIO no está instalado: las temperaturas por núcleo no están disponibles. Recomendamos instalarlo:",
            "PawnIO パッケージがインストールされていないため、コアごとの温度は表示できません。インストールをおすすめします:",
            "未安装 PawnIO 软件包，因此无法显示每核温度。建议安装：",
        },
        ["pawn.power"] = new[]
        {
            "Пакет PawnIO не установлен: от сети показана только сумма CPU + GPU, без экрана и других компонентов. Рекомендуем установить:",
            "The PawnIO package is not installed: on AC only CPU + GPU is shown, without the display and other parts. We recommend installing it:",
            "Le paquet PawnIO n'est pas installé : sur secteur, seule la somme CPU + GPU est affichée, sans l'écran ni les autres composants. Nous recommandons de l'installer :",
            "El paquete PawnIO no está instalado: con corriente solo se muestra CPU + GPU, sin la pantalla ni otros componentes. Recomendamos instalarlo:",
            "PawnIO パッケージがインストールされていないため、AC 電源では CPU + GPU の合計のみ表示します (画面などは含みません)。インストールをおすすめします:",
            "未安装 PawnIO 软件包：交流供电时仅显示 CPU + GPU，不含屏幕等其他部件。建议安装：",
        },
        ["ov.total"] = new[] { "Всего", "Total", "Total", "Total", "合計", "总计" },
        ["pw.mode"] = new[] { "Режим Windows", "Windows mode", "Mode Windows", "Modo de Windows", "Windows モード", "Windows 模式" },
        ["pw.eff"] = new[] { "Экономия", "Efficiency", "Économie", "Ahorro", "省電力", "节能" },
        ["pw.bal"] = new[] { "Баланс", "Balanced", "Équilibré", "Equilibrado", "バランス", "平衡" },
        ["pw.perf"] = new[] { "Максимум", "Maximum", "Maximum", "Máximo", "最大", "最大" },
        ["pw.battery"] = new[] { "От батареи · {0}%", "On battery · {0}%", "Sur batterie · {0} %", "Con batería · {0}%", "バッテリー · {0}%", "电池供电 · {0}%" },
        ["pw.ac"] = new[] { "От сети · батарея {0}%", "On AC · battery {0}%", "Sur secteur · batterie {0} %", "Con corriente · batería {0}%", "AC 電源 · バッテリー {0}%", "交流供电 · 电池 {0}%" },
        ["pw.acnobat"] = new[] { "От сети", "On AC", "Sur secteur", "Con corriente", "AC 電源", "交流供电" },
        ["pw.charging"] = new[] { "От сети · батарея {0}%, заряд +{1} Вт", "On AC · battery {0}%, charging +{1} W", "Sur secteur · batterie {0} %, charge +{1} W", "Con corriente · batería {0}%, cargando +{1} W", "AC 電源 · バッテリー {0}%、充電 +{1} W", "交流供电 · 电池 {0}%，充电 +{1} W" },
        ["pw.left"] = new[] { "осталось {0}", "{0} left", "reste {0}", "quedan {0}", "残り {0}", "剩余 {0}" },
        ["pw.cpugpu"] = new[] { "из них CPU {0} Вт · GPU {1} Вт", "of which CPU {0} W · GPU {1} W", "dont CPU {0} W · GPU {1} W", "de ello CPU {0} W · GPU {1} W", "うち CPU {0} W · GPU {1} W", "其中 CPU {0} W · GPU {1} W" },
        ["pw.tip"] = new[]
        {
            "На батарее — потребление всего ноутбука по датчику батареи. От сети — счётчик платформы Intel (RAPL PSYS) через драйвер PawnIO: вся платформа, не только процессор. Без PawnIO от сети — только CPU + GPU.",
            "On battery: the whole laptop's draw from the battery gauge. On AC: Intel's platform energy counter (RAPL PSYS) via the PawnIO driver — the whole platform, not just the CPU. Without PawnIO on AC: CPU + GPU only.",
            "Sur batterie : jauge de batterie. Sur secteur : compteur plateforme Intel (RAPL PSYS) via PawnIO. Sans PawnIO : CPU + GPU.",
            "Con batería: medidor de la batería. Con corriente: contador de plataforma de Intel (RAPL PSYS) vía PawnIO. Sin PawnIO: CPU + GPU.",
            "バッテリー時はバッテリー計、AC 時は Intel プラットフォーム カウンター（RAPL PSYS、PawnIO 経由）。PawnIO がなければ CPU + GPU。",
            "电池供电时用电池计量；交流供电时用 Intel 平台计数器（RAPL PSYS，经 PawnIO）；无 PawnIO 时仅 CPU + GPU。",
        },
        ["u.h"] = new[] { "ч", "h", "h", "h", "時間", "小时" },
        ["u.min"] = new[] { "мин", "min", "min", "min", "分", "分钟" },
        ["opt.theme"] = new[] { "Тема", "Theme", "Thème", "Tema", "テーマ", "主题" },
        ["theme.add"] = new[] { "Добавить тему…", "Add theme…", "Ajouter un thème…", "Añadir tema…", "テーマを追加…", "添加主题…" },
        ["theme.folder"] = new[] { "Папка тем", "Themes folder", "Dossier des thèmes", "Carpeta de temas", "テーマ フォルダー", "主题文件夹" },
        ["theme.export"] = new[] { "Сохранить текущую в файл…", "Save current as file…", "Enregistrer le thème actuel…", "Guardar el actual como archivo…", "現在のテーマをファイルに保存…", "将当前主题保存为文件…" },
        ["theme.filter"] = new[] { "Тема Alien Helper", "Alien Helper theme", "Thème Alien Helper", "Tema de Alien Helper", "Alien Helper テーマ", "Alien Helper 主题" },
        ["theme.err"] = new[]
        {
            "Эту тему не получилось загрузить: {0}",
            "This theme could not be loaded: {0}",
            "Impossible de charger ce thème : {0}",
            "No se pudo cargar este tema: {0}",
            "このテーマを読み込めませんでした: {0}",
            "无法加载此主题: {0}",
        },
        ["theme.warn"] = new[]
        {
            "Тема добавлена. Обратите внимание:\n\n{0}",
            "The theme was added. Note:\n\n{0}",
            "Le thème a été ajouté. À noter :\n\n{0}",
            "Se añadió el tema. Tenga en cuenta:\n\n{0}",
            "テーマを追加しました。注意:\n\n{0}",
            "已添加主题。请注意:\n\n{0}",
        },
        ["theme.dark"] = new[] { "Тёмная", "Dark", "Sombre", "Oscuro", "ダーク", "深色" },
        ["theme.light"] = new[] { "Светлая", "Light", "Clair", "Claro", "ライト", "浅色" },
        ["theme.phosphor"] = new[] { "Терминал", "Terminal", "Terminal", "Terminal", "ターミナル", "终端" },
        ["theme.strict"] = new[] { "Строгая", "Strict", "Sobre", "Sobrio", "シンプル", "简洁" },
        ["opt.numbers"] = new[] { "Только цифры, без графиков", "Numbers only, no graphs", "Chiffres seuls, sans graphiques", "Solo números, sin gráficos", "数値のみ（グラフなし）", "仅数字，不显示图表" },
        ["card.net"] = new[] { "Сеть", "Network", "Réseau", "Red", "ネットワーク", "网络" },
        ["card.nonet"] = new[] { "нет активных подключений", "no active connections", "aucune connexion active", "sin conexiones activas", "アクティブな接続なし", "无活动连接" },
        ["net.link"] = new[] { "канал {0}", "link {0}", "lien {0}", "enlace {0}", "リンク {0}", "链路 {0}" },
        ["ch.net"] = new[] { "Сеть — скорость", "Network — speed", "Réseau — débit", "Red — velocidad", "ネットワーク — 速度", "网络 — 速度" },
        ["u.kbps"] = new[] { "Кбит/с", "Kbps", "Kbit/s", "Kbps", "Kbps", "Kbps" },
        ["u.mbps"] = new[] { "Мбит/с", "Mbps", "Mbit/s", "Mbps", "Mbps", "Mbps" },
        ["u.gbps"] = new[] { "Гбит/с", "Gbps", "Gbit/s", "Gbps", "Gbps", "Gbps" },
        ["card.nodisks"] = new[] { "нет данных", "no data", "aucune donnée", "sin datos", "データなし", "无数据" },
        ["card.mode"] = new[] { "Режим", "Mode", "Mode", "Modo", "モード", "模式" },
        ["mode.now"] = new[] { "сейчас: {0}", "now: {0}", "actuel : {0}", "actual: {0}", "現在: {0}", "当前: {0}" },
        // mode descriptions (tooltips), keyed by firmware profile id
        ["mode.tip.A0"] = new[]
        {
            "Баланс производительности, шума и температуры. Обычный режим на каждый день.",
            "Balance of performance, noise and temperature. The everyday mode.",
            "Équilibre entre performances, bruit et température. Le mode du quotidien.",
            "Equilibrio entre rendimiento, ruido y temperatura. El modo para el día a día.",
            "性能・騒音・温度のバランス。普段使いのモード。",
            "性能、噪音与温度的平衡。日常使用模式。",
        },
        ["mode.tip.A1"] = new[]
        {
            "Чуть выше лимиты мощности, чем в Balanced; вентиляторы раскручиваются раньше.",
            "Slightly higher power limits than Balanced; fans spin up earlier.",
            "Limites de puissance un peu plus élevées qu'en Balanced ; les ventilateurs accélèrent plus tôt.",
            "Límites de potencia algo más altos que Balanced; los ventiladores aceleran antes.",
            "Balanced より少し高い電力制限。ファンは早めに回り始めます。",
            "功耗上限略高于 Balanced；风扇更早提速。",
        },
        ["mode.tip.A2"] = new[]
        {
            "Приоритет — холодный корпус: мощность ниже, вентиляторы активнее (буст 75%). Удобно держать на коленях.",
            "Keeps the chassis cool: lower power, more active fans (75% boost). Comfortable on your lap.",
            "Châssis plus frais : puissance réduite, ventilateurs plus actifs (boost 75 %). Confortable sur les genoux.",
            "Mantiene el chasis fresco: menos potencia, ventiladores más activos (impulso 75 %). Cómodo en el regazo.",
            "本体を涼しく保つ：電力を抑え、ファンを積極的に回します（ブースト 75%）。膝の上でも快適。",
            "优先保持机身凉爽：功耗更低，风扇更积极（增强 75%）。放在腿上更舒适。",
        },
        ["mode.tip.A3"] = new[]
        {
            "Минимум шума: вентиляторы медленнее, мощность CPU и GPU ограничена сильнее.",
            "Lowest noise: slower fans, CPU and GPU power limited more.",
            "Bruit minimal : ventilateurs plus lents, puissance CPU et GPU plus limitée.",
            "Mínimo ruido: ventiladores más lentos, potencia de CPU y GPU más limitada.",
            "静音重視：ファンを遅くし、CPU と GPU の電力を強めに制限。",
            "最低噪音：风扇更慢，CPU 和 GPU 功耗限制更严。",
        },
        ["mode.tip.A4"] = new[]
        {
            "Повышенные лимиты мощности CPU и GPU для игр и тяжёлых задач; вентиляторы громче.",
            "Higher CPU and GPU power limits for games and heavy work; louder fans.",
            "Limites CPU et GPU plus élevées pour les jeux et les tâches lourdes ; ventilateurs plus bruyants.",
            "Límites de CPU y GPU más altos para juegos y tareas pesadas; ventiladores más ruidosos.",
            "ゲームや重い作業向けに CPU と GPU の電力制限を引き上げ。ファンは大きめ。",
            "提高 CPU 和 GPU 功耗上限，适合游戏和重负载；风扇更响。",
        },
        ["mode.tip.AB"] = new[]
        {
            "Alien Mode: максимальная мощность — лимиты CPU и GPU на максимум, вентиляторы на 100%. Громко.",
            "Alien Mode: maximum power — CPU and GPU limits at max, fans at 100%. Loud.",
            "Alien Mode : puissance maximale — limites CPU et GPU au maximum, ventilateurs à 100 %. Bruyant.",
            "Alien Mode: potencia máxima — límites de CPU y GPU al máximo, ventiladores al 100%. Ruidoso.",
            "Alien Mode：最大パワー — CPU と GPU の制限を最大に、ファンは 100%。騒音大。",
            "Alien Mode：最大性能 — CPU 和 GPU 上限拉满，风扇 100%。噪音大。",
        },
        ["mode.unknown"] = new[] { "не определён", "unknown", "inconnu", "desconocido", "不明", "未知" },
        ["boost"] = new[] { "Буст", "Boost", "Boost", "Impulso", "ブースト", "增强" },
        ["boost.split"] = new[] { "Раздельно", "Split", "Séparé", "Separado", "個別", "分开" },
        ["boost.split.tip"] = new[]
        {
            "Раздельный буст для вентиляторов CPU и видеокарты (экспертный режим)",
            "Separate boost for the CPU and GPU fans (expert mode)",
            "Boost séparé pour les ventilateurs CPU et GPU (mode expert)",
            "Impulso separado para los ventiladores de CPU y GPU (modo experto)",
            "CPU と GPU のファンを個別にブースト (エキスパート モード)",
            "CPU 与显卡风扇分别增强（专家模式）",
        },
        ["boost.tip"] = new[]
        {
            "Поднимает обороты над кривой BIOS. 0 — обороты выбирает BIOS.",
            "Raises fan speed above the BIOS curve. 0 lets the BIOS decide.",
            "Augmente la vitesse des ventilateurs au-dessus de la courbe du BIOS. 0 = BIOS.",
            "Sube la velocidad de los ventiladores sobre la curva del BIOS. 0 = BIOS.",
            "BIOS のカーブよりファン回転数を上げます。0 で BIOS 制御。",
            "在 BIOS 曲线之上提高风扇转速。0 = 由 BIOS 控制。",
        },
        ["auto"] = new[] { "авто", "auto", "auto", "auto", "自動", "自动" },
        ["card.screen"] = new[] { "Яркость экрана", "Screen brightness", "Luminosité de l'écran", "Brillo de pantalla", "画面の明るさ", "屏幕亮度" },
        ["card.lights"] = new[] { "Подсветка", "Lighting", "Éclairage", "Iluminación", "ライティング", "灯光" },
        ["light.kb"] = new[] { "Клавиатура", "Keyboard", "Clavier", "Teclado", "キーボード", "键盘" },
        ["light.body"] = new[] { "Корпус", "Chassis", "Châssis", "Chasis", "本体", "机身" },
        ["opt.overlay"] = new[]
        {
            "Оверлей в играх (Ctrl+Shift+O)", "Overlay in games (Ctrl+Shift+O)", "Overlay en jeu (Ctrl+Shift+O)",
            "Superposición en juegos (Ctrl+Shift+O)", "ゲーム中のオーバーレイ (Ctrl+Shift+O)", "游戏内叠加层 (Ctrl+Shift+O)",
        },
        ["opt.overlay.show"] = new[]
        {
            "Показывать оверлей", "Show overlay", "Afficher l'overlay",
            "Mostrar superposición", "オーバーレイを表示", "显示叠加层",
        },
        ["opt.guardlights"] = new[] { "Защищать подсветку от AWCC", "Guard lighting from AWCC", "Protéger l'éclairage d'AWCC", "Proteger la iluminación de AWCC", "ライティングを AWCC から守る", "防止 AWCC 更改灯光" },
        ["opt.guardlights.tip"] = new[]
        {
            "Alienware Command Center иногда сам включает подсветку. После входа, разблокировки, выхода из сна и смены режима программа несколько раз в течение минуты возвращает свою подсветку (или выключенную, если она выключена здесь).",
            "Alienware Command Center sometimes turns the lighting on by itself. After sign-in, unlock, sleep and mode changes the app puts its own lighting back several times over a minute (or keeps it off if it is off here).",
            "Alienware Command Center rallume parfois l'éclairage de lui-même. Après la connexion, le déverrouillage, la veille et les changements de mode, l'application rétablit son éclairage plusieurs fois pendant une minute (ou le garde éteint s'il est éteint ici).",
            "Alienware Command Center a veces enciende la iluminación por su cuenta. Tras iniciar sesión, desbloquear, salir de la suspensión y cambiar de modo, la aplicación restaura su iluminación varias veces durante un minuto (o la mantiene apagada si aquí está apagada).",
            "Alienware Command Center は時々ライティングを勝手にオンにします。サインイン、ロック解除、スリープ解除、モード変更の後、アプリは 1 分間に数回自分のライティングを戻します (ここでオフならオフのまま)。",
            "Alienware Command Center 有时会自行打开灯光。登录、解锁、从睡眠唤醒和切换模式后，应用会在一分钟内多次恢复自己的灯光 (如果这里关闭了灯光，则保持关闭)。",
        },
        ["opt.awccoff"] = new[] { "Отключить AWCC", "Turn off AWCC", "Désactiver AWCC", "Desactivar AWCC", "AWCC をオフにする", "关闭 AWCC" },
        ["opt.awccoff.tip"] = new[]
        {
            "Останавливает и отключает службу Dell TechHub, которая запускает агенты Alienware Command Center (режимы, питание, подсветку). Так AWCC больше не вмешивается. Ничего не удаляется: выключите тумблер, и служба вернётся. Пока AWCC отключён, не работают диагностика и обновления SupportAssist / Dell Update.",
            "Stops and disables the Dell TechHub service that starts Alienware Command Center's agents (modes, power, lighting), so AWCC no longer interferes. Nothing is uninstalled: switch it back and the service returns. While AWCC is off, SupportAssist / Dell Update diagnostics and updates do not work.",
            "Arrête et désactive le service Dell TechHub qui lance les agents d'Alienware Command Center (modes, alimentation, éclairage) : AWCC n'interfère plus. Rien n'est désinstallé : désactivez l'option et le service revient. Tant qu'AWCC est désactivé, les diagnostics et mises à jour SupportAssist / Dell Update ne fonctionnent pas.",
            "Detiene y desactiva el servicio Dell TechHub que inicia los agentes de Alienware Command Center (modos, energía, iluminación), así AWCC deja de interferir. No se desinstala nada: desactive la opción y el servicio vuelve. Mientras AWCC está desactivado, los diagnósticos y actualizaciones de SupportAssist / Dell Update no funcionan.",
            "Alienware Command Center のエージェント (モード、電力、ライティング) を起動する Dell TechHub サービスを停止・無効化し、AWCC が干渉しないようにします。何もアンインストールしません: オフに戻すとサービスも戻ります。AWCC がオフの間、SupportAssist / Dell Update の診断と更新は動作しません。",
            "停止并禁用启动 Alienware Command Center 代理 (模式、电源、灯光) 的 Dell TechHub 服务，使 AWCC 不再干扰。不会卸载任何内容: 关闭此选项即可恢复服务。AWCC 关闭期间，SupportAssist / Dell Update 的诊断和更新无法使用。",
        },
        ["awccoff.confirm"] = new[]
        {
            "Остановить и отключить службу Dell TechHub вместе с агентами AWCC?\n\nРежимы, вентиляторы и подсветка продолжат работать через Alien Helper. Пока AWCC отключён, не работают диагностика и обновления SupportAssist / Dell Update. Вернуть можно этим же тумблером.",
            "Stop and disable the Dell TechHub service along with AWCC's agents?\n\nModes, fans and lighting keep working through Alien Helper. While AWCC is off, SupportAssist / Dell Update diagnostics and updates do not work. The same switch brings it back.",
            "Arrêter et désactiver le service Dell TechHub ainsi que les agents d'AWCC ?\n\nLes modes, les ventilateurs et l'éclairage continuent de fonctionner via Alien Helper. Tant qu'AWCC est désactivé, les diagnostics et mises à jour SupportAssist / Dell Update ne fonctionnent pas. La même option le rétablit.",
            "¿Detener y desactivar el servicio Dell TechHub junto con los agentes de AWCC?\n\nLos modos, ventiladores e iluminación siguen funcionando a través de Alien Helper. Mientras AWCC está desactivado, los diagnósticos y actualizaciones de SupportAssist / Dell Update no funcionan. La misma opción lo restaura.",
            "Dell TechHub サービスと AWCC のエージェントを停止・無効化しますか?\n\nモード、ファン、ライティングは Alien Helper で引き続き動作します。AWCC がオフの間、SupportAssist / Dell Update の診断と更新は動作しません。同じスイッチで元に戻せます。",
            "停止并禁用 Dell TechHub 服务及 AWCC 代理?\n\n模式、风扇和灯光将继续通过 Alien Helper 工作。AWCC 关闭期间，SupportAssist / Dell Update 的诊断和更新无法使用。用同一开关即可恢复。",
        },
        ["err.awcc"] = new[]
        {
            "Не удалось изменить службу Dell TechHub. Подробности в журнале.",
            "Could not change the Dell TechHub service. See the log for details.",
            "Impossible de modifier le service Dell TechHub. Voir le journal.",
            "No se pudo cambiar el servicio Dell TechHub. Consulte el registro.",
            "Dell TechHub サービスを変更できませんでした。詳細はログを参照してください。",
            "无法更改 Dell TechHub 服务。详情请查看日志。",
        },
        ["opt.holdgpu"] = new[] { "Удерживать мощность GPU в Alien Mode", "Hold GPU power in Alien Mode", "Maintenir la puissance du GPU en Alien Mode", "Mantener la potencia de la GPU en Alien Mode", "Alien Mode で GPU の電力を維持", "在 Alien Mode 下保持 GPU 功率" },
        ["opt.holdgpu.tip"] = new[]
        {
            "Иногда в Alien Mode видеокарта под нагрузкой остаётся на низком лимите мощности (80 Вт, как в Balanced), хотя режим не менялся. Если это длится больше 10 секунд, программа сама переключает режим туда и обратно. Горячую видеокарту (от 87 °C) не трогает.",
            "Now and then in Alien Mode the GPU stays at the low power limit under load (80 W, as in Balanced) although the mode did not change. When that lasts over 10 seconds, the app switches the mode away and back. A hot GPU (87 °C and up) is left alone.",
            "Parfois en Alien Mode, le GPU reste à la limite de puissance basse sous charge (80 W, comme en Balanced) alors que le mode n'a pas changé. Si cela dure plus de 10 secondes, l'application change de mode puis y revient. Un GPU chaud (87 °C et plus) n'est pas touché.",
            "A veces en Alien Mode la GPU se queda en el límite de potencia bajo con carga (80 W, como en Balanced) aunque el modo no cambió. Si dura más de 10 segundos, la aplicación cambia de modo y vuelve. Una GPU caliente (87 °C o más) no se toca.",
            "Alien Mode でも、モードが変わっていないのに負荷時の GPU が低い電力上限 (Balanced と同じ 80 W) のままになることがあります。10 秒以上続くと、アプリがモードを切り替えて戻します。高温の GPU (87 °C 以上) には触れません。",
            "在 Alien Mode 下，有时模式没有变化，但 GPU 在负载下仍停留在较低的功率上限 (80 W，与 Balanced 相同)。持续超过 10 秒时，应用会切换模式再切回。GPU 过热 (87 °C 及以上) 时不做处理。",
        },
        ["opt.apply"] = new[]
        {
            "Применять настройки при запуске", "Apply settings on start", "Appliquer les réglages au démarrage",
            "Aplicar ajustes al iniciar", "起動時に設定を適用", "启动时应用设置",
        },
        ["opt.minimized"] = new[]
        {
            "Запускать свёрнутым (в трее)", "Start minimized (to tray)", "Démarrer réduit (dans la zone de notification)",
            "Iniciar minimizado (en la bandeja)", "最小化して起動 (トレイ)", "启动时最小化（托盘）",
        },
        ["opt.autostart"] = new[] { "Запускать при входе в Windows", "Start with Windows", "Lancer avec Windows", "Iniciar con Windows", "Windows 起動時に開始", "随 Windows 启动" },
        ["opt.lang"] = new[] { "Язык", "Language", "Langue", "Idioma", "言語", "语言" },
        ["elev.title"] = new[] { "Нужны права администратора", "Administrator rights needed", "Droits d'administrateur requis", "Se necesitan derechos de administrador", "管理者権限が必要です", "需要管理员权限" },
        ["elev.text"] = new[]
        {
            "Переключение режимов питания, управление вентиляторами, температуры ядер и FPS в играх работают только с правами администратора: прошивка ноутбука открывает эти функции только администратору.\n\nСейчас Windows покажет запрос UAC. Если отказаться, программа запустится в ограниченном режиме: мониторинг NVIDIA, загрузка процессора, сеть и графики.",
            "Switching power modes, fan control, per-core temperatures and in-game FPS need administrator rights: the laptop firmware offers these functions to administrators only.\n\nWindows will now show the UAC prompt. If you decline, the app starts in limited mode: NVIDIA monitoring, CPU load, network and charts.",
            "Le changement de mode d'alimentation, le contrôle des ventilateurs, les températures par cœur et les FPS en jeu nécessitent les droits d'administrateur : le firmware du portable ne les propose qu'aux administrateurs.\n\nWindows va afficher l'invite UAC. Si vous refusez, l'application démarre en mode limité : surveillance NVIDIA, charge du processeur, réseau et graphiques.",
            "Cambiar los modos de energía, controlar los ventiladores, las temperaturas por núcleo y los FPS en juegos requieren derechos de administrador: el firmware del portátil solo ofrece estas funciones a los administradores.\n\nWindows mostrará ahora el aviso de UAC. Si lo rechaza, la aplicación se inicia en modo limitado: monitorización de NVIDIA, carga del procesador, red y gráficos.",
            "電源モードの切り替え、ファン制御、コアごとの温度、ゲームの FPS には管理者権限が必要です。ノート PC のファームウェアはこれらの機能を管理者にのみ提供します。\n\nこれから Windows が UAC の確認を表示します。拒否すると、アプリは制限モードで起動します: NVIDIA の監視、CPU 負荷、ネットワーク、グラフ。",
            "切换电源模式、控制风扇、每核温度和游戏 FPS 需要管理员权限：笔记本固件只向管理员提供这些功能。\n\nWindows 现在将显示 UAC 提示。如果拒绝，应用将以受限模式启动：NVIDIA 监控、处理器负载、网络和图表。",
        },
        ["elev.continue"] = new[] { "Продолжить", "Continue", "Continuer", "Continuar", "続行", "继续" },
        ["elev.limited"] = new[] { "Без прав", "Without rights", "Sans droits", "Sin derechos", "権限なしで", "不使用权限" },
        ["elev.dontshow"] = new[] { "Больше не показывать", "Don't show again", "Ne plus afficher", "No volver a mostrar", "今後表示しない", "不再显示" },
        ["limited.title"] = new[] { "Ограниченный режим", "Limited mode", "Mode limité", "Modo limitado", "制限モード", "受限模式" },
        ["limited.text"] = new[]
        {
            "Нет прав администратора: режимы, вентиляторы, температуры процессора и FPS недоступны.",
            "No administrator rights: modes, fans, CPU temperatures and FPS are unavailable.",
            "Pas de droits d'administrateur : modes, ventilateurs, températures du processeur et FPS indisponibles.",
            "Sin derechos de administrador: modos, ventiladores, temperaturas del procesador y FPS no disponibles.",
            "管理者権限がありません: モード、ファン、CPU 温度、FPS は使用できません。",
            "没有管理员权限：模式、风扇、处理器温度和 FPS 不可用。",
        },
        ["limited.restart"] = new[] { "Перезапустить с правами администратора", "Restart as administrator", "Redémarrer en administrateur", "Reiniciar como administrador", "管理者として再起動", "以管理员身份重新启动" },
        ["opt.autostart.windows"] = new[] { "Автозапуск — в параметрах Windows", "Start with Windows — in Windows Settings", "Lancement avec Windows — dans les Paramètres", "Inicio con Windows — en Configuración", "Windows 起動時の開始 — Windows の設定で", "随 Windows 启动 — 在 Windows 设置中" },
        ["err.task"] = new[]
        {
            "Не удалось изменить автозапуск.", "Could not change start with Windows.",
            "Impossible de modifier le lancement avec Windows.", "No se pudo cambiar el inicio con Windows.",
            "Windows 起動時の開始を変更できませんでした。", "无法修改随 Windows 启动设置。",
        },

        // keyboard editor
        ["btn.keys"] = new[] { "Клавиши", "Keys", "Touches", "Teclas", "キー", "按键" },
        ["kb.title"] = new[] { "Подсветка клавиш", "Key lighting", "Éclairage des touches", "Iluminación de teclas", "キー ライティング", "按键灯光" },
        ["kb.templates"] = new[] { "Шаблоны", "Templates", "Modèles", "Plantillas", "テンプレート", "模板" },
        ["kb.fill"] = new[] { "Залить все", "Fill all", "Tout remplir", "Rellenar todo", "すべて塗る", "全部填充" },
        ["kb.clear"] = new[] { "Очистить", "Clear", "Effacer", "Borrar", "クリア", "清除" },
        ["kb.off"] = new[] { "Выключить клавишу", "Key off", "Touche éteinte", "Tecla apagada", "消灯", "关闭按键" },
        ["kb.brightness"] = new[] { "Яркость", "Brightness", "Luminosité", "Brillo", "明るさ", "亮度" },
        ["kb.hint"] = new[]
        {
            "ЛКМ — покрасить (можно вести мышью), ПКМ — выключить клавишу", "Left click paints (drag to paint several), right click turns a key off",
            "Clic gauche : peindre (glisser pour plusieurs), clic droit : éteindre", "Clic izquierdo pinta (arrastra para varias), clic derecho apaga",
            "左クリックで塗る (ドラッグで連続)、右クリックで消灯", "左键上色（可拖动），右键关闭按键",
        },
        ["kb.calib"] = new[] { "Настройка раскладки", "Fix key mapping", "Corriger le mappage", "Corregir mapeo", "キー割り当て調整", "校准按键" },
        ["kb.calib.hint"] = new[]
        {
            "Выберите клавишу, затем ◀ ▶, пока на ноутбуке не загорится белым именно она",
            "Pick a key, then use ◀ ▶ until exactly that key lights white",
            "Choisissez une touche, puis ◀ ▶ jusqu'à ce qu'elle s'allume en blanc",
            "Elige una tecla y usa ◀ ▶ hasta que se ilumine en blanco",
            "キーを選び、そのキーが白く光るまで ◀ ▶ で調整",
            "选择按键，用 ◀ ▶ 调整直到该键亮白色",
        },
        ["kb.template"] = new[] { "Шаблон", "Template", "Modèle", "Plantilla", "テンプレート", "模板" },
        ["kb.none"] = new[] { "без шаблона", "no template", "aucun modèle", "sin plantilla", "テンプレートなし", "无模板" },
        ["kb.new"] = new[] { "Новый", "New", "Nouveau", "Nueva", "新規", "新建" },
        ["kb.saveas"] = new[] { "Сохранить как…", "Save as…", "Enregistrer sous…", "Guardar como…", "名前を付けて保存…", "另存为…" },
        ["kb.save"] = new[] { "Сохранить", "Save", "Enregistrer", "Guardar", "保存", "保存" },
        ["kb.delete"] = new[] { "Удалить", "Delete", "Supprimer", "Eliminar", "削除", "删除" },
        ["kb.restore"] = new[] { "Вернуть стандартные", "Restore built-ins", "Restaurer les modèles", "Restaurar predefinidas", "標準テンプレートを戻す", "恢复内置模板" },
        ["kb.save.name"] = new[] { "Название шаблона", "Template name", "Nom du modèle", "Nombre de la plantilla", "テンプレート名", "模板名称" },
        ["kb.save.default"] = new[] { "Мой шаблон {0}", "My template {0}", "Mon modèle {0}", "Mi plantilla {0}", "マイテンプレート {0}", "我的模板 {0}" },
        ["kb.anim"] = new[] { "Анимация", "Animation", "Animation", "Animación", "アニメーション", "动画" },
        ["kb.anim.tip"] = new[]
        {
            "Огонь, Океан и Радуга двигаются; выключите, чтобы оставить их статичными",
            "Fire, Ocean and Rainbow move; turn off to keep them still",
            "Feu, Océan et Arc-en-ciel s'animent ; désactivez pour les figer",
            "Fuego, Océano y Arcoíris se mueven; desactívalo para dejarlos fijos",
            "ファイア、オーシャン、レインボーが動きます。オフで静止",
            "火焰、海洋和彩虹会动；关闭则保持静止",
        },
        ["kb.reset"] = new[] { "Сброс", "Reset", "Réinitialiser", "Restablecer", "リセット", "重置" },
        ["kb.done"] = new[] { "Готово", "Done", "Terminé", "Listo", "完了", "完成" },
        ["tpl.wasd"] = new[] { "WASD", "WASD", "WASD", "WASD", "WASD", "WASD" },
        ["tpl.fps"] = new[] { "FPS", "FPS", "FPS", "FPS", "FPS", "FPS" },
        ["tpl.moba"] = new[] { "MOBA", "MOBA", "MOBA", "MOBA", "MOBA", "MOBA" },
        ["tpl.cat"] = new[] { "Котик", "Cat", "Chat", "Gato", "ネコ", "猫咪" },
        ["tpl.heart"] = new[] { "Сердце", "Heart", "Cœur", "Corazón", "ハート", "爱心" },
        ["tpl.rainbow"] = new[] { "Радуга", "Rainbow", "Arc-en-ciel", "Arcoíris", "レインボー", "彩虹" },
        ["tpl.fire"] = new[] { "Огонь", "Fire", "Feu", "Fuego", "ファイア", "火焰" },
        ["tpl.ocean"] = new[] { "Океан", "Ocean", "Océan", "Océano", "オーシャン", "海洋" },

        // about, disclaimer, hardware checks
        ["hk.title"] = new[] { "Горячие клавиши", "Hotkeys", "Raccourcis", "Atajos", "ホットキー", "快捷键" },
        ["hk.overlay"] = new[] { "Оверлей: показать / скрыть", "Overlay: show / hide", "Overlay : afficher / masquer", "Superposición: ver / ocultar", "オーバーレイ表示切替", "叠加层：显示 / 隐藏" },
        ["hk.style"] = new[] { "Оверлей: сменить вид", "Overlay: next style", "Overlay : style suivant", "Superposición: siguiente estilo", "オーバーレイ: 次の表示", "叠加层：切换样式" },
        ["hk.mode"] = new[] { "Сменить режим (по кругу)", "Next power mode", "Mode suivant", "Siguiente modo", "次のモード", "切换模式（循环）" },
        ["hk.none"] = new[] { "нет", "none", "aucun", "ninguno", "なし", "无" },
        ["hk.press"] = new[] { "Нажмите сочетание…", "Press a combination…", "Appuyez sur une combinaison…", "Pulsa una combinación…", "キーを押してください…", "请按组合键…" },
        ["hk.hint"] = new[] { "Щёлкните поле и нажмите сочетание с Ctrl, Alt или Shift. Backspace — очистить, Esc — отмена.", "Click a field and press a combination with Ctrl, Alt or Shift. Backspace clears, Esc cancels.", "Cliquez sur un champ et appuyez sur une combinaison avec Ctrl, Alt ou Maj. Retour arrière efface, Échap annule.", "Haz clic en un campo y pulsa una combinación con Ctrl, Alt o Mayús. Retroceso borra, Esc cancela.", "欄をクリックし、Ctrl・Alt・Shift を含む組み合わせを押します。Backspace で消去、Esc で取消。", "点击输入框并按下包含 Ctrl、Alt 或 Shift 的组合键。Backspace 清除，Esc 取消。" },
        ["hk.busy"] = new[] { "Сочетание {0} уже занято другой программой — выберите другое.", "{0} is already used by another app — pick another one.", "{0} est déjà utilisé par une autre application — choisissez-en un autre.", "{0} ya lo usa otra aplicación; elige otra.", "{0} は別のアプリで使用中です。別の組み合わせを選んでください。", "{0} 已被其他程序占用，请选择其他组合。" },
        ["settings"] = new[] { "Настройки", "Settings", "Paramètres", "Ajustes", "設定", "设置" },
        ["settings.start"] = new[] { "Запуск", "Start-up", "Démarrage", "Inicio", "起動", "启动" },
        ["about"] = new[] { "О программе", "About", "À propos", "Acerca de", "このアプリについて", "关于" },
        ["disclaimer.short"] = new[]
        {
            "Независимый проект, не связан с Dell", "Independent project, not affiliated with Dell",
            "Projet indépendant, non affilié à Dell", "Proyecto independiente, sin relación con Dell",
            "Dell とは無関係の独立プロジェクト", "独立项目，与 Dell 无关",
        },
        ["about.text"] = new[]
        {
            "Alien Helper {0}\n\nНезависимый проект. Не связан с Dell Technologies Inc., не одобрен и не спонсируется ею. Alienware, AlienFX, Alienware Command Center и Area-51 — товарные знаки Dell Inc.; они упоминаются только для указания совместимого оборудования.\n\nПрограмма меняет режимы питания, обороты вентиляторов и подсветку через интерфейсы прошивки ноутбука. Используйте на свой риск.\n\nСторонние компоненты и лицензии: THIRD-PARTY-NOTICES.txt рядом с программой.",
            "Alien Helper {0}\n\nIndependent project. Not affiliated with, endorsed or sponsored by Dell Technologies Inc. Alienware, AlienFX, Alienware Command Center and Area-51 are trademarks of Dell Inc.; they are mentioned only to identify compatible hardware.\n\nThe app changes power modes, fan speeds and lighting through the laptop's firmware interfaces. Use at your own risk.\n\nThird-party components and licenses: THIRD-PARTY-NOTICES.txt next to the app.",
            "Alien Helper {0}\n\nProjet indépendant. Non affilié à Dell Technologies Inc., ni approuvé ni sponsorisé par elle. Alienware, AlienFX, Alienware Command Center et Area-51 sont des marques de Dell Inc. ; elles ne sont citées que pour identifier le matériel compatible.\n\nL'application modifie les modes d'alimentation, la vitesse des ventilateurs et l'éclairage via les interfaces du firmware. Utilisation à vos risques.\n\nComposants tiers et licences : THIRD-PARTY-NOTICES.txt à côté de l'application.",
            "Alien Helper {0}\n\nProyecto independiente. Sin relación con Dell Technologies Inc., ni respaldado ni patrocinado por ella. Alienware, AlienFX, Alienware Command Center y Area-51 son marcas de Dell Inc.; solo se mencionan para identificar el hardware compatible.\n\nLa aplicación cambia los modos de energía, la velocidad de los ventiladores y la iluminación mediante las interfaces del firmware. Úsala bajo tu responsabilidad.\n\nComponentes de terceros y licencias: THIRD-PARTY-NOTICES.txt junto a la aplicación.",
            "Alien Helper {0}\n\n独立プロジェクトです。Dell Technologies Inc. とは提携・承認・後援の関係はありません。Alienware、AlienFX、Alienware Command Center、Area-51 は Dell Inc. の商標で、対応ハードウェアを示すためにのみ記載しています。\n\n本アプリはノート PC のファームウェア インターフェイスを通じて電源モード、ファン速度、ライティングを変更します。自己責任でご使用ください。\n\nサードパーティ コンポーネントとライセンス: アプリと同じ場所の THIRD-PARTY-NOTICES.txt。",
            "Alien Helper {0}\n\n独立项目。与 Dell Technologies Inc. 无隶属关系，未获其认可或赞助。Alienware、AlienFX、Alienware Command Center 和 Area-51 是 Dell Inc. 的商标，仅用于说明兼容的硬件。\n\n本应用通过笔记本固件接口更改电源模式、风扇转速和灯光。使用风险自负。\n\n第三方组件和许可证：应用旁的 THIRD-PARTY-NOTICES.txt。",
        },
        ["about.devices"] = new[]
        {
            "Проверено на: {0}.\n\nДолжно работать (те же интерфейсы прошивки, не проверялось): ноутбуки Alienware с Alienware Command Center 2020 года и новее — m15/m17 R3–R5, x14/x15/x17, m16/m18, Alienware 16/18 Area-51, 16 Aurora / 16X Aurora. Dell G15/G16 — только режимы и вентиляторы.\nКарта клавиш снята с 16-дюймового Area-51: на других моделях отдельные клавиши могут подсвечиваться не там.",
            "Tested on: {0}.\n\nShould work (same firmware interfaces, not tested): Alienware laptops with Alienware Command Center from 2020 on — m15/m17 R3–R5, x14/x15/x17, m16/m18, Alienware 16/18 Area-51, 16 Aurora / 16X Aurora. Dell G15/G16 — power modes and fans only.\nThe key map was taken from the 16\" Area-51: on other models some keys may light up in the wrong place.",
            "Testé sur : {0}.\n\nDevrait fonctionner (mêmes interfaces firmware, non testé) : portables Alienware avec Alienware Command Center depuis 2020 — m15/m17 R3–R5, x14/x15/x17, m16/m18, Alienware 16/18 Area-51, 16 Aurora / 16X Aurora. Dell G15/G16 — modes d'alimentation et ventilateurs uniquement.\nLa carte des touches provient du 16\" Area-51 : sur d'autres modèles, certaines touches peuvent s'allumer au mauvais endroit.",
            "Probado en: {0}.\n\nDebería funcionar (mismas interfaces de firmware, sin probar): portátiles Alienware con Alienware Command Center desde 2020 — m15/m17 R3–R5, x14/x15/x17, m16/m18, Alienware 16/18 Area-51, 16 Aurora / 16X Aurora. Dell G15/G16: solo modos de energía y ventiladores.\nEl mapa de teclas es del Area-51 de 16\": en otros modelos algunas teclas pueden iluminarse en otro sitio.",
            "動作確認済み: {0}。\n\n動作するはず（同じファームウェア インターフェイス、未確認）: 2020 年以降の Alienware Command Center 搭載 Alienware ノート — m15/m17 R3–R5、x14/x15/x17、m16/m18、Alienware 16/18 Area-51、16 Aurora / 16X Aurora。Dell G15/G16 は電源モードとファンのみ。\nキー配置は 16 インチ Area-51 から取得したため、他のモデルでは一部のキーが別の位置で点灯する場合があります。",
            "已测试：{0}。\n\n应可使用（相同固件接口，未测试）：2020 年及以后配备 Alienware Command Center 的 Alienware 笔记本 — m15/m17 R3–R5、x14/x15/x17、m16/m18、Alienware 16/18 Area-51、16 Aurora / 16X Aurora。Dell G15/G16 仅支持电源模式和风扇。\n按键映射取自 16 英寸 Area-51，在其他型号上部分按键可能亮在错误位置。",
        },
        ["ch.ssd"] = new[] { "SSD — температура", "SSD — temperature", "SSD — température", "SSD — temperatura", "SSD — 温度", "SSD — 温度" },
        ["ch.ssdio"] = new[] { "SSD — операции в секунду (IOPS)", "SSD — operations per second (IOPS)", "SSD — opérations par seconde (IOPS)", "SSD — operaciones por segundo (IOPS)", "SSD — 毎秒操作数 (IOPS)", "SSD — 每秒操作数 (IOPS)" },
        ["io.read"] = new[] { "Чтение", "Read", "Lecture", "Lectura", "読み取り", "读取" },
        ["io.write"] = new[] { "Запись", "Write", "Écriture", "Escritura", "書き込み", "写入" },
        ["io.short"] = new[] { "Ч {0} · З {1}", "R {0} · W {1}", "L {0} · É {1}", "L {0} · E {1}", "R {0} · W {1}", "读 {0} · 写 {1}" },
        ["about.version"] = new[] { "Версия {0}", "Version {0}", "Version {0}", "Versión {0}", "バージョン {0}", "版本 {0}" },
        ["about.support"] = new[] { "Поддержать проект", "Support the project", "Soutenir le projet", "Apoyar el proyecto", "プロジェクトを支援", "支持本项目" },
        ["about.source"] = new[] { "Исходный код", "Source code", "Code source", "Código fuente", "ソース コード", "源代码" },
        ["about.contact"] = new[] { "Связь с автором: {0}", "Contact the author: {0}", "Contacter l'auteur : {0}", "Contacto con el autor: {0}", "作者への連絡: {0}", "联系作者：{0}" },
        ["diag.title"] = new[] { "Диагностика", "Diagnostics", "Diagnostic", "Diagnóstico", "診断", "诊断" },
        ["diag.collect"] = new[] { "Собрать отчёт", "Collect report", "Créer un rapport", "Crear informe", "レポートを作成", "生成报告" },
        ["diag.mail"] = new[] { "Написать автору", "Email the author", "Écrire à l'auteur", "Escribir al autor", "作者にメール", "给作者写信" },
        ["diag.logs"] = new[] { "Папка логов", "Logs folder", "Journaux", "Registros", "ログ フォルダー", "日志文件夹" },
        ["diag.hint"] = new[]
        {
            "Если программа вылетает или работает неправильно: откройте окно с проблемой и нажмите «Собрать отчёт». На рабочем столе появится ZIP с логами, настройками и снимками окон программы (только её окон). Отправьте его автору.",
            "If the app crashes or misbehaves: open the window with the problem and press “Collect report”. A ZIP with logs, settings and snapshots of the app's own windows appears on the Desktop. Send it to the author.",
            "Si l'application plante ou fonctionne mal : ouvrez la fenêtre concernée et cliquez sur « Créer un rapport ». Un ZIP avec journaux, réglages et captures des seules fenêtres de l'application apparaît sur le Bureau. Envoyez-le à l'auteur.",
            "Si la aplicación falla o funciona mal: abre la ventana con el problema y pulsa «Crear informe». En el Escritorio aparecerá un ZIP con registros, ajustes y capturas solo de las ventanas de la aplicación. Envíalo al autor.",
            "アプリが落ちる・動作がおかしい場合は、問題のウィンドウを開いて「レポートを作成」を押してください。ログ、設定、アプリ自身のウィンドウのスナップショットを含む ZIP がデスクトップに作成されます。作者に送ってください。",
            "如果程序崩溃或工作异常：打开出问题的窗口并点击“生成报告”。桌面上会生成包含日志、设置和本程序窗口截图（仅本程序窗口）的 ZIP，请发送给作者。",
        },
        ["diag.saved"] = new[]
        {
            "Отчёт сохранён:\n{0}\n\nОтправьте его на {1}. Открыть письмо сейчас?",
            "Report saved:\n{0}\n\nPlease send it to {1}. Open an email now?",
            "Rapport enregistré :\n{0}\n\nEnvoyez-le à {1}. Ouvrir un e-mail maintenant ?",
            "Informe guardado:\n{0}\n\nEnvíalo a {1}. ¿Abrir un correo ahora?",
            "レポートを保存しました:\n{0}\n\n{1} に送ってください。今すぐメールを開きますか?",
            "报告已保存：\n{0}\n\n请发送至 {1}。现在打开邮件吗？",
        },
        ["diag.crashed"] = new[]
        {
            "В прошлый раз Alien Helper завершился с ошибкой. Собрать отчёт для автора?",
            "Alien Helper closed with an error last time. Collect a report for the author?",
            "Alien Helper s'est fermé sur une erreur la dernière fois. Créer un rapport pour l'auteur ?",
            "Alien Helper se cerró con un error la última vez. ¿Crear un informe para el autor?",
            "前回 Alien Helper はエラーで終了しました。作者向けのレポートを作成しますか?",
            "Alien Helper 上次因错误退出。要为作者生成报告吗？",
        },
        ["hw.limited.fold"] = new[] { "Свернуть", "Collapse", "Réduire", "Contraer", "折りたたむ", "收起" },
        ["hw.limited.title"] = new[] { "Ограниченный режим", "Limited mode", "Mode limité", "Modo limitado", "制限モード", "受限模式" },
        ["hw.limited"] = new[]
        {
            "{0} — не Alienware, поэтому режимы питания, вентиляторы, подсветка и датчики Alienware недоступны. Работает мониторинг: процессор, видеокарта NVIDIA, память, диски, сеть и питание.",
            "{0} is not an Alienware, so power modes, fans, lighting and Alienware sensors are unavailable. Monitoring works: CPU, NVIDIA GPU, memory, disks, network and power.",
            "{0} n'est pas un Alienware : modes d'alimentation, ventilateurs, éclairage et capteurs Alienware sont indisponibles. La surveillance fonctionne : processeur, GPU NVIDIA, mémoire, disques, réseau et alimentation.",
            "{0} no es un Alienware, así que los modos de energía, ventiladores, iluminación y sensores de Alienware no están disponibles. La monitorización funciona: CPU, GPU NVIDIA, memoria, discos, red y energía.",
            "{0} は Alienware ではないため、電源モード、ファン、ライティング、Alienware のセンサーは使用できません。監視は動作します: CPU、NVIDIA GPU、メモリ、ディスク、ネットワーク、電源。",
            "{0} 不是 Alienware，因此电源模式、风扇、灯光和 Alienware 传感器不可用。监控可用：CPU、NVIDIA GPU、内存、磁盘、网络和电源。",
        },
        ["hw.limited.admin"] = new[]
        {
            "Не найден интерфейс управления Alienware (AWCC WMI): режимы питания, вентиляторы и подсветка недоступны. Если это Alienware, запустите программу от имени администратора.",
            "The Alienware control interface (AWCC WMI) was not found: power modes, fans and lighting are unavailable. If this is an Alienware, run the app as administrator.",
            "Interface de contrôle Alienware (AWCC WMI) introuvable : modes d'alimentation, ventilateurs et éclairage indisponibles. S'il s'agit d'un Alienware, lancez l'application en tant qu'administrateur.",
            "No se encontró la interfaz de control de Alienware (AWCC WMI): modos de energía, ventiladores e iluminación no disponibles. Si es un Alienware, ejecuta la aplicación como administrador.",
            "Alienware 制御インターフェイス (AWCC WMI) が見つかりません: 電源モード、ファン、ライティングは使用できません。Alienware の場合は管理者として実行してください。",
            "未找到 Alienware 控制接口（AWCC WMI）：电源模式、风扇和灯光不可用。如果这是 Alienware，请以管理员身份运行。",
        },
        ["hw.untested"] = new[]
        {
            "Модель «{0}» не проверялась. Alien Helper проверен только на Alienware 16 Area-51 (AA16250): на других моделях подсветка клавиш и часть функций могут работать неправильно.",
            "The model \"{0}\" has not been tested. Alien Helper is tested only on the Alienware 16 Area-51 (AA16250); on other models per-key lighting and some features may not work correctly.",
            "Le modèle « {0} » n'a pas été testé. Alien Helper n'est testé que sur l'Alienware 16 Area-51 (AA16250) ; sur d'autres modèles, l'éclairage par touche et certaines fonctions peuvent mal fonctionner.",
            "El modelo «{0}» no se ha probado. Alien Helper solo está probado en el Alienware 16 Area-51 (AA16250); en otros modelos la iluminación por tecla y algunas funciones pueden no funcionar bien.",
            "モデル「{0}」は未検証です。Alien Helper は Alienware 16 Area-51 (AA16250) でのみ検証済みで、他のモデルではキーごとのライティングなどが正しく動作しない場合があります。",
            "型号“{0}”未经测试。Alien Helper 仅在 Alienware 16 Area-51（AA16250）上测试过；在其他型号上，按键灯光和部分功能可能无法正常工作。",
        },

        // tray
        ["tray.boost"] = new[] { "Буст вентиляторов", "Fan boost", "Boost ventilateurs", "Impulso de ventiladores", "ファン ブースト", "风扇增强" },
        ["tray.autobios"] = new[] { "Авто (BIOS)", "Auto (BIOS)", "Auto (BIOS)", "Auto (BIOS)", "自動 (BIOS)", "自动 (BIOS)" },
        ["tray.noawcc"] = new[] { "AWCC WMI недоступен", "AWCC WMI unavailable", "AWCC WMI indisponible", "AWCC WMI no disponible", "AWCC WMI を利用できません", "AWCC WMI 不可用" },
        ["tray.toggle"] = new[]
        {
            "Показать / скрыть", "Show / hide", "Afficher / masquer",
            "Mostrar / ocultar", "表示 / 非表示", "显示 / 隐藏",
        },
        ["tray.autogames"] = new[] { "Автоматически в играх", "Automatically in games", "Automatique en jeu", "Automático en juegos", "ゲームで自動表示", "游戏中自动显示" },
        ["corner.0"] = new[] { "Слева сверху", "Top left", "En haut à gauche", "Arriba a la izquierda", "左上", "左上" },
        ["corner.1"] = new[] { "Справа сверху", "Top right", "En haut à droite", "Arriba a la derecha", "右上", "右上" },
        ["corner.2"] = new[] { "Слева снизу", "Bottom left", "En bas à gauche", "Abajo a la izquierda", "左下", "左下" },
        ["corner.3"] = new[] { "Справа снизу", "Bottom right", "En bas à droite", "Abajo a la derecha", "右下", "右下" },
        ["tray.open"] = new[] { "Открыть", "Open", "Ouvrir", "Abrir", "開く", "打开" },
        ["tray.exit"] = new[] { "Выход", "Exit", "Quitter", "Salir", "終了", "退出" },

        // charts
        ["ch.temps"] = new[] { "Температуры", "Temperatures", "Températures", "Temperaturas", "温度", "温度" },
        ["ch.fans"] = new[] { "Вентиляторы", "Fans", "Ventilateurs", "Ventiladores", "ファン", "风扇" },
        ["ch.clocks"] = new[] { "Частоты", "Clocks", "Fréquences", "Frecuencias", "クロック", "频率" },
        ["ch.memory"] = new[] { "Память", "Memory", "Mémoire", "Memoria", "メモリ", "内存" },
        ["ch.cores"] = new[] { "Ядра процессора — {0}%   ·   {1} P + {2} E", "CPU cores — {0}%   ·   {1} P + {2} E", "Cœurs du processeur — {0} %   ·   {1} P + {2} E", "Núcleos de CPU — {0}%   ·   {1} P + {2} E", "CPU コア — {0}%   ·   {1} P + {2} E", "CPU 核心 — {0}%   ·   {1} P + {2} E" },
        ["ch.cores.flat"] = new[] { "Ядра процессора — {0}%   ·   {1} потоков", "CPU cores — {0}%   ·   {1} threads", "Cœurs du processeur — {0} %   ·   {1} threads", "Núcleos de CPU — {0}%   ·   {1} hilos", "CPU コア — {0}%   ·   {1} スレッド", "CPU 核心 — {0}%   ·   {1} 线程" },
        ["ch.coretemp.intel"] = new[]
        {
            "Температура отдельных ядер читается только на процессорах Intel",
            "Per-core temperatures are read on Intel CPUs only",
            "Les températures par cœur ne sont lues que sur les processeurs Intel",
            "Las temperaturas por núcleo solo se leen en CPU Intel",
            "コアごとの温度は Intel CPU でのみ読み取れます",
            "仅 Intel 处理器可读取各核心温度",
        },
        ["ch.cores.loading"] = new[] { "Ядра процессора — загрузка…", "CPU cores — loading…", "Cœurs du processeur — chargement…", "Núcleos de CPU — cargando…", "CPU コア — 読み込み中…", "CPU 核心 — 加载中…" },
        ["ch.coretemp"] = new[]
        {
            "Температура ядер: нужен драйвер PawnIO (ставится с CapFrameX, FanControl или LibreHardwareMonitor)",
            "Core temperatures need the PawnIO driver (installed by CapFrameX, FanControl or LibreHardwareMonitor)",
            "Températures des cœurs : pilote PawnIO requis (installé par CapFrameX, FanControl ou LibreHardwareMonitor)",
            "Temperaturas de núcleo: requiere el controlador PawnIO (lo instalan CapFrameX, FanControl o LibreHardwareMonitor)",
            "コア温度には PawnIO ドライバーが必要です（CapFrameX、FanControl、LibreHardwareMonitor がインストール）",
            "核心温度需要 PawnIO 驱动（随 CapFrameX、FanControl 或 LibreHardwareMonitor 安装）",
        },
        ["ch.coretemp.on"] = new[]
        {
            "Температура ядер — датчики процессора (через PawnIO), макс. {0}°", "Core temperatures from the CPU sensors (via PawnIO), max {0}°",
            "Températures des cœurs (capteurs CPU via PawnIO), max {0}°", "Temperaturas de núcleo (sensores de la CPU vía PawnIO), máx. {0}°",
            "コア温度（CPU センサー、PawnIO 経由）最大 {0}°", "核心温度（CPU 传感器，经 PawnIO）最高 {0}°",
        },
        ["ch.core"] = new[] { "{0}-ядро {1}", "{0}-core {1}", "Cœur {0} {1}", "Núcleo {0} {1}", "{0} コア {1}", "{0} 核 {1}" },
        ["ch.last"] = new[] { "последние {0} мин", "last {0} min", "dernières {0} min", "últimos {0} min", "直近 {0} 分", "最近 {0} 分钟" },

        // overlay styles and moving
        ["ov.style"] = new[] { "Вид оверлея", "Overlay style", "Style d'overlay", "Estilo", "表示スタイル", "叠加层样式" },
        ["ov.s0"] = new[] { "Полный", "Full", "Complet", "Completo", "フル", "完整" },
        ["ov.s1"] = new[] { "Мини", "Mini", "Mini", "Mini", "ミニ", "迷你" },
        ["ov.s2"] = new[] { "Строка", "Bar", "Barre", "Barra", "バー", "横条" },
        ["ov.s3"] = new[] { "Иконки", "Icons", "Icônes", "Iconos", "アイコン", "图标" },
        ["ov.move"] = new[]
        {
            "Двигать: Ctrl+Shift+Alt+стрелки", "Move: Ctrl+Shift+Alt+arrows", "Déplacer : Ctrl+Maj+Alt+flèches",
            "Mover: Ctrl+Mayús+Alt+flechas", "移動: Ctrl+Shift+Alt+矢印", "移动：Ctrl+Shift+Alt+方向键",
        },
        ["ov.reset"] = new[] { "Сбросить положение", "Reset position", "Réinitialiser la position", "Restablecer posición", "位置をリセット", "重置位置" },

        // overlay
        ["ov.noframes"] = new[] { "нет кадров", "no frames", "aucune image", "sin fotogramas", "フレームなし", "无帧" },
        ["ov.frame"] = new[] { "КАДР", "FRAME", "IMAGE", "FOTOG.", "フレーム", "帧时" },
        ["ov.max"] = new[] { "макс {0}", "max {0}", "max {0}", "máx {0}", "最大 {0}", "最大 {0}" },

        // units
        ["u.rpm"] = new[] { "об/мин", "rpm", "tr/min", "rpm", "rpm", "rpm" },
        ["u.ghz"] = new[] { "ГГц", "GHz", "GHz", "GHz", "GHz", "GHz" },
        ["u.mhz"] = new[] { "МГц", "MHz", "MHz", "MHz", "MHz", "MHz" },
        ["u.w"] = new[] { "Вт", "W", "W", "W", "W", "W" },
        ["u.gb"] = new[] { "ГБ", "GB", "Go", "GB", "GB", "GB" },
        ["u.ms"] = new[] { "мс", "ms", "ms", "ms", "ms", "ms" },
        ["u.sec"] = new[] { "с", "s", "s", "s", "秒", "秒" },
        ["opt.poll"] = new[] { "Обновление метрик", "Metrics refresh", "Actualisation des mesures", "Actualización de métricas", "メトリクスの更新", "指标刷新" },
        ["opt.poll.hint"] = new[] { "Чаще — плавнее графики и оверлей, но чуть больше нагрузка. Графики всё равно показывают последние 5 минут.", "More often means smoother charts and overlay, at a little more load. Charts still cover the last 5 minutes.", "Plus souvent : graphiques et overlay plus fluides, un peu plus de charge. Les graphiques couvrent toujours les 5 dernières minutes.", "Más a menudo: gráficos y superposición más fluidos, con algo más de carga. Los gráficos siguen mostrando los últimos 5 minutos.", "頻度を上げるとグラフとオーバーレイが滑らかになりますが、負荷が少し増えます。グラフは常に直近 5 分を表示します。", "越频繁，图表和叠加层越流畅，但负载略高。图表始终显示最近 5 分钟。" },
        ["u.k"] = new[] { "к", "k", "k", "k", "k", "k" },
    };
}
