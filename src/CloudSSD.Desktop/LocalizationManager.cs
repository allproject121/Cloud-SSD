using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Data;

namespace CloudSSD.Desktop;

public static class LocalizationManager
{
    public static readonly string[] SupportedLanguages =
    [
        "Hinglish",
        "English",
        "हिन्दी (Hindi)",
        "Español (Spanish)",
        "Français (French)",
        "Deutsch (German)",
        "日本語 (Japanese)"
    ];

    public static string CurrentLanguage { get; private set; } = "Hinglish";

    private static readonly string ConfigPath = ResolveConfigPath();

    private static string ResolveConfigPath()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var ssdPath = Path.Combine(localAppData, "Cloud-SSD", "language.txt");
        var legacyPath = Path.Combine(localAppData, "CloudPlayX", "language.txt");
        if (File.Exists(legacyPath) && !File.Exists(ssdPath)) return legacyPath;
        return ssdPath;
    }

    private static readonly DependencyProperty OriginalTextProperty =
        DependencyProperty.RegisterAttached("OriginalText", typeof(string), typeof(LocalizationManager));

    /// <summary>What the last pass put on screen, to tell "we translated it" from "the app changed it".</summary>
    private static readonly DependencyProperty TranslatedTextProperty =
        DependencyProperty.RegisterAttached("TranslatedText", typeof(string), typeof(LocalizationManager));

    public static event Action? LanguageChanged;

    static LocalizationManager()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var saved = File.ReadAllText(ConfigPath).Trim();
                if (!string.IsNullOrEmpty(saved)) CurrentLanguage = saved;
            }
        }
        catch { }

        // Automatically translate every window and dialog when it opens
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler((s, e) =>
        {
            if (s is Window w)
            {
                var origTitle = (string)w.GetValue(OriginalTextProperty);
                if (origTitle == null)
                {
                    origTitle = w.Title;
                    w.SetValue(OriginalTextProperty, origTitle);
                }
                if (!string.IsNullOrEmpty(origTitle))
                {
                    w.Title = T(origTitle);
                }
                TranslateTree(w);
            }
        }));
    }

    public static void SetLanguage(string lang)
    {
        if (string.IsNullOrWhiteSpace(lang)) return;
        CurrentLanguage = lang;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
            File.WriteAllText(ConfigPath, CurrentLanguage);
        }
        catch { }

        LanguageChanged?.Invoke();
        if (Application.Current?.MainWindow != null)
        {
            TranslateTree(Application.Current.MainWindow);
        }
    }

    public static string T(string english)
    {
        if (string.IsNullOrEmpty(english)) return english;
        var trimmed = english.Trim();
        if (CurrentLanguage == "English") return english;

        if (Translations.TryGetValue(CurrentLanguage, out var dict) && dict.TryGetValue(trimmed, out var trans))
        {
            return english.Replace(trimmed, trans);
        }
        return english;
    }

    private static bool _isTranslating;

    public static void TranslateTree(DependencyObject? root)
    {
        if (root == null || _isTranslating) return;
        try
        {
            _isTranslating = true;
            TranslateNode(root, 0);
        }
        catch { }
        finally
        {
            _isTranslating = false;
        }
    }

    private static void TranslateNode(DependencyObject? root, int depth)
    {
        if (root == null || depth > 30) return;

        try
        {
            if (root is TextBlock tb)
            {
                var binding = BindingOperations.GetBindingExpression(tb, TextBlock.TextProperty);
                if (binding == null)
                {
                    var orig = (string)tb.GetValue(OriginalTextProperty);
                    // Text that is neither the stored original nor what we last showed was changed by the app
                    // itself (the selected item of a list, a status line), so it becomes the new original.
                    if (orig == null || (tb.Text != orig && tb.Text != (string)tb.GetValue(TranslatedTextProperty)))
                    {
                        orig = tb.Text;
                        tb.SetValue(OriginalTextProperty, orig);
                    }
                    if (!string.IsNullOrEmpty(orig))
                    {
                        var trans = T(orig);
                        tb.SetValue(TranslatedTextProperty, trans);
                        if (tb.Text != trans) tb.Text = trans;
                    }
                }
            }
            else if (root is Button btn && btn.Content is string btnStr)
            {
                var binding = BindingOperations.GetBindingExpression(btn, ContentControl.ContentProperty);
                if (binding == null)
                {
                    var orig = (string)btn.GetValue(OriginalTextProperty);
                    if (orig == null)
                    {
                        orig = btnStr;
                        btn.SetValue(OriginalTextProperty, orig);
                    }
                    var trans = T(orig);
                    if (!Equals(btn.Content, trans)) btn.Content = trans;
                }
                // The control shows its text through an inner TextBlock. Translating that one too would pin it
                // to the text it had on the first pass, and the control would then ignore later language changes.
                return;
            }
            else if (root is CheckBox cb && cb.Content is string cbStr)
            {
                var binding = BindingOperations.GetBindingExpression(cb, ContentControl.ContentProperty);
                if (binding == null)
                {
                    var orig = (string)cb.GetValue(OriginalTextProperty);
                    if (orig == null)
                    {
                        orig = cbStr;
                        cb.SetValue(OriginalTextProperty, orig);
                    }
                    var trans = T(orig);
                    if (!Equals(cb.Content, trans)) cb.Content = trans;
                }
                return;
            }
            else if (root is RadioButton rb && rb.Content is string rbStr)
            {
                var binding = BindingOperations.GetBindingExpression(rb, ContentControl.ContentProperty);
                if (binding == null)
                {
                    var orig = (string)rb.GetValue(OriginalTextProperty);
                    if (orig == null)
                    {
                        orig = rbStr;
                        rb.SetValue(OriginalTextProperty, orig);
                    }
                    var trans = T(orig);
                    if (!Equals(rb.Content, trans)) rb.Content = trans;
                }
                return;
            }
            else if (root is Label lbl && lbl.Content is string lblStr)
            {
                var binding = BindingOperations.GetBindingExpression(lbl, ContentControl.ContentProperty);
                if (binding == null)
                {
                    var orig = (string)lbl.GetValue(OriginalTextProperty);
                    if (orig == null)
                    {
                        orig = lblStr;
                        lbl.SetValue(OriginalTextProperty, orig);
                    }
                    var trans = T(orig);
                    if (!Equals(lbl.Content, trans)) lbl.Content = trans;
                }
                return;
            }
            else if (root is HeaderedContentControl hcc && hcc.Header is string hStr)
            {
                var orig = (string)hcc.GetValue(OriginalTextProperty);
                if (orig == null)
                {
                    orig = hStr;
                    hcc.SetValue(OriginalTextProperty, orig);
                }
                var trans = T(orig);
                if (!Equals(hcc.Header, trans)) hcc.Header = trans;
            }

            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                TranslateNode(VisualTreeHelper.GetChild(root, i), depth + 1);
            }
        }
        catch { }
    }

    private static readonly Dictionary<string, Dictionary<string, string>> Translations = new()
    {
        ["Hinglish"] = new(StringComparer.OrdinalIgnoreCase)
        {
            // Sidebar Navigation
            ["Dashboard"] = "Dashboard (Home)",
            ["Cloud Drives"] = "Cloud Drives (Storage)",
            ["Game Library"] = "Game Library (Games)",
            ["Phone (Mobile Hub)"] = "Phone / Mobile",
            ["Mobile Hub"] = "Phone / Mobile",
            ["Accelerator"] = "Speed Booster",
            ["Smart Cache"] = "Smart SSD Cache",
            ["Performance"] = "Live Performance",
            ["Settings"] = "Settings",
            ["Diagnostics"] = "Health & Diagnostics",
            ["Cloud-to-SSD engine"] = "Cloud-to-SSD Storage Engine",
            ["Cloud-to-SSD virtual drive engine"] = "Cloud-to-SSD Virtual Drive Engine",
            ["Restore drives when Cloud-SSD starts"] = "App khulte hi Drive auto-connect karein",
            ["Mount automatically when Cloud-SSD starts"] = "App shuru hote hi drive auto-mount karein",
            ["Welcome to Cloud-SSD"] = "Cloud-SSD me aapka swagat hai",
            ["Language / भाषा"] = "Language / भाषा",

            // Cloud Drives View & Multi-Provider
            ["Sign in with Google Drive"] = "Google Drive Connect Karein",
            ["Connect Cloud Account"] = "Cloud Account Jodein",
            ["Add virtual drive"] = "Nayi Virtual Drive Jodein",
            ["Virtual drives"] = "Aapki Virtual Drives",
            ["No drives yet. Connect an account, then add a drive."] = "Abhi koi drive nahi hai. Pehle account connect karein.",
            ["Connected accounts"] = "Jude Hue Cloud Accounts",
            ["None yet."] = "Abhi koi account nahi hai.",
            ["Mount"] = "Connect Karein (Mount)",
            ["Unmount"] = "Disconnect Karein (Unmount)",
            ["Open"] = "Explorer me Kholein",
            ["Refresh listing"] = "Refresh Karein",
            ["Disconnect"] = "Disconnect Karein",
            ["Remove"] = "Hatao",
            ["Cloud providers"] = "Supported Cloud Providers",
            ["Connect Cloud Account (Google / OneDrive / WebDAV)"] = "Cloud Account Jodein (Google / OneDrive / WebDAV)",
            ["Google Drive"] = "Google Drive",
            ["Microsoft OneDrive"] = "Microsoft OneDrive",
            ["WebDAV / Nextcloud"] = "WebDAV / Nextcloud",
            ["Sign in with Microsoft"] = "Microsoft Account se Sign In Karein",
            ["Connect WebDAV Storage"] = "WebDAV Storage Connect Karein",
            ["Personal OneDrive"] = "Personal OneDrive (Niji)",
            ["Business / School / SharePoint"] = "Business / School / SharePoint",
            ["SERVER WEBDAV URL"] = "SERVER KA WEBDAV URL",
            ["SERVICE TYPE / VENDOR"] = "SERVICE TYPE / VENDOR",
            ["USERNAME"] = "USERNAME",
            ["PASSWORD OR APP TOKEN"] = "PASSWORD YA APP TOKEN",
            ["Read-only access (protects cloud files; no writing or installing on it)"] = "Read-only access (cloud files safe rahenge)",

            // Game Library View
            ["Add game"] = "Naya Game Jodein",
            ["No games added yet. Mount a drive, then add a game folder."] = "Abhi koi game nahi hai. Pehle drive connect karein.",
            ["PLAY"] = "PLAY KAREIN ▶",
            ["Optimize & Prepare"] = "Optimize & Prepare (Fast Load)",
            ["Ready to play"] = "Khelne ke liye Ready",
            ["Needs preparation"] = "Preparation zaroori hai",
            ["Running"] = "Game chal raha hai",
            ["Cloud Direct"] = "Cloud Direct",
            ["Hybrid Optimized"] = "Hybrid Optimized (Tez)",
            ["Full Local"] = "Full Local (SSD)",
            ["Verified on this PC"] = "Is PC par Verified hai",
            ["No blockers found"] = "Sab theek hai (Ready)",
            ["Install locally…"] = "Local install karein…",
            ["Game settings"] = "Game Settings",
            ["Remove from library"] = "Library se hatayein",

            // Save Game Backup & Sync
            ["CLOUD SAVE-GAME BACKUP & SYNC"] = "CLOUD SAVE-GAME AUTO BACKUP & SYNC",
            ["Automatically back up save games to cloud when game exits"] = "Game band hote hi save files cloud par auto-backup karein",
            ["SAVE FOLDER LOCATION"] = "GAME SAVE FOLDER KA LOCATION",
            ["Auto-Detect"] = "Auto-Detect (Pata Lagao)",
            ["Browse…"] = "Browse Karein…",
            ["Backup Saves Now"] = "Abhi Save Backup Karein",
            ["Restore from Cloud"] = "Cloud se Restore Karein",

            // Dashboard View
            ["Get started"] = "Shuruat Karein",
            ["CONNECTION HEALTH"] = "CONNECTION STATUS",
            ["CACHE ON SSD"] = "SSD BUFFER CACHE",
            ["CACHE HIT RATE (THIS RUN)"] = "SSD SPEED HIT RATE",
            ["ENGINE"] = "STREAMING ENGINE",
            ["Network throughput (rclone, last 2 min)"] = "Live Internet Speed (rclone, last 2 min)",
            ["Storage"] = "Storage Space",
            ["Save settings"] = "Settings Save Karein",
            ["Open data folder"] = "Data Folder Kholein",

            // Performance View
            ["Transfer rate"] = "Live Transfer Speed",
            ["Cache hit rate"] = "Local SSD Speed Ratio",
            ["Average read wait"] = "Average Response Latency",
            ["Application reads"] = "Application Reads / Sec",
            ["Totals since start"] = "Ab tak ka Total Data",

            // Settings → Storage: auto cloud switch, moved files, what is using the space
            ["⚡ Move Files to Cloud Drive Now"] = "⚡ Files Abhi Cloud Drive par Bhejein",
            ["Save Settings"] = "Settings Save Karein",
            ["⚡ SMART SPACE SAVER & AUTO CLOUD OFFLOAD"] = "⚡ JAGAH BACHAO & AUTO CLOUD SWITCH",
            ["When laptop storage drops below your limit, local files automatically switch to Cloud Drive (Z:)."] = "Jab laptop ki jagah aapki limit se kam ho, to files apne aap Cloud Drive (Z:) par chali jaati hain.",
            ["Enable Auto Cloud Switch on Low Storage"] = "Jagah kam hone par Auto Cloud Switch chalu rakhein",
            ["Trigger when laptop free space is below:"] = "Tab chale jab laptop me khali jagah isse kam ho:",
            ["First delete old temporary files (untouched for 7 days, not in use)"] = "Pehle purani temporary files hatao (7 din se anchhui, use me nahi)",
            ["Also move old big files from Downloads (over 100 MB, untouched for 7 days; folders are never moved). A shortcut stays in Downloads."] = "Downloads ki purani badi files bhi cloud par bhejo (100 MB se badi, 7 din se anchhui; folder kabhi nahi jaate). Downloads me shortcut reh jata hai.",
            ["Also empty Recycle Bin items deleted more than 30 days ago"] = "Recycle Bin ki 30 din se purani cheezein bhi hatao",
            ["While a game or a phone stream is running, nothing new is cleaned or moved and cloud uploads run slowly."] = "Game ya phone stream chalte waqt kuch naya saaf ya move nahi hota, aur cloud upload dheema chalta hai.",
            ["FILES MOVED FROM DOWNLOADS TO THE CLOUD"] = "DOWNLOADS SE CLOUD PAR GAYI FILES",
            ["Refresh"] = "Dobara Dekhein",
            ["Bring back"] = "Wapas Lao",
            ["What is using the space"] = "Jagah kaun le raha hai",
            ["Shows the biggest folders and files on the Windows drive, so you can see what to delete or move. Nothing is changed."] = "Windows drive ke sabse bade folder aur files dikhata hai, taaki aap dekh sako kya hatana ya bhejna hai. Kuch badla nahi jata.",
            ["Scan now"] = "Abhi Jaancho",
            ["{0} GB free: {1} GB short of the {2} GB limit. Auto mode cleans and moves what it can; the list below shows what else takes space."] = "{0} GB khali: {2} GB ki limit se {1} GB kam. Auto mode jo saaf ya move kar sakta hai karta hai; neeche ki list dikhati hai aur kya jagah le raha hai.",
            ["✓ Storage Safe: {0} GB free (Threshold: {1} GB)"] = "✓ Jagah theek hai: {0} GB khali (limit: {1} GB)",
            ["Reading the drive… this takes a minute or two."] = "Drive padh raha hai… ek-do minute lagenge.",
            ["Biggest folders and files on {0}, read in {1} s. Sizes are what they really take on the disk."] = "{0} ke sabse bade folder aur files, {1} second me padhe. Size wahi hai jo disk par asal me lagta hai.",
            ["Could not read the drive: {0}"] = "Drive padh nahi paya: {0}",
            ["Could not open the folder: {0}"] = "Folder khul nahi paya: {0}",
            ["No file from Downloads is on the cloud right now."] = "Abhi Downloads ki koi file cloud par nahi hai.",
            ["{0} file(s) from Downloads are on the cloud ({1})."] = "Downloads ki {0} file(s) cloud par hain ({1}).",
            ["Bringing {0} back from the cloud…"] = "{0} ko cloud se wapas la raha hai…",
            ["Windows itself. Leave it."] = "Ye Windows khud hai. Ise mat chhedo.",
            ["Windows' virtual memory. Leave it."] = "Windows ki virtual memory. Ise mat chhedo.",
            ["Used for sleep / fast start-up."] = "Sleep / fast start-up ke liye.",
            ["Recycle Bin. Empty it to get this space back."] = "Recycle Bin. Khali karo to ye jagah wapas milegi.",
            ["Installed programs. Uninstall what you do not use."] = "Install kiye programs. Jo use nahi karte unhe uninstall karo.",
            ["Temporary files. Cloud-SSD removes the old ones by itself."] = "Temporary files. Purani Cloud-SSD khud hata deta hai.",
            ["Download cache of a developer tool. Safe to delete; it downloads again."] = "Developer tool ka download cache. Hatana safe hai; wo dobara download kar leta hai.",
            ["A cache. Usually safe to delete; the app fills it again."] = "Ye cache hai. Aksar hatana safe hai; app ise dobara bhar leta hai.",
            ["Your downloads."] = "Aapke downloads.",

            // PC Memory View
            ["Auto mode: when less than 30% of RAM is free, move idle background apps out of RAM"] = "Auto mode: jab 30% se kam RAM khali ho, to bekaar pade background apps ko RAM se hata do",
            ["Run now"] = "Abhi Chalao",
            ["Game mode: when a game or a phone stream starts, free RAM at once and close the apps chosen here"] = "Game mode: game ya phone stream shuru hote hi RAM turant khali karo aur yahan chune apps band karo",
            ["Open those apps again when the game is over"] = "Game khatam hone par un apps ko wapas kholo",
            ["Game mode acts for a game started from the Game Library and for an Android emulator in front (BlueStacks and the like). Each app is asked to close the normal way, so one with unsaved work stays open and asks you."] = "Game mode tab chalta hai jab Game Library se game shuru ho ya koi Android emulator (BlueStacks jaisa) saamne ho. Har app normal tareeke se band hota hai, isliye jisme bina save kiya kaam hai wo khula rehta hai aur aapse poochta hai.",
            ["Close in games"] = "Game me band karo",
            ["RAM auto mode"] = "RAM auto mode",
            ["Game mode"] = "Game mode",
            ["During the game {0} app(s) were closed and {1} of RAM was freed."] = "Game ke dauraan {0} app(s) band kiye aur {1} RAM khali ki.",
            ["Opened again: {0}."] = "Wapas khole: {0}.",
            ["Game mode is off."] = "Game mode band hai.",
            ["When a game starts, RAM is freed at once. No app is set to close: pick apps with 'Close in games' in the list below."] = "Game shuru hote hi RAM turant khali hoti hai. Band karne ke liye koi app nahi chuna: neeche ki list me 'Game me band karo' se chuniye.",
            ["When a game starts, RAM is freed at once and these apps are closed:"] = "Game shuru hote hi RAM turant khali hoti hai aur ye apps band hote hain:",
            ["A game is running now."] = "Abhi game chal raha hai.",
            ["Last game {0}: closed {1} app(s), free RAM {2} → {3}."] = "Pichhla game {0}: {1} app(s) band kiye, khali RAM {2} → {3}.",
            ["Auto modes"] = "Auto modes",
            ["Storage auto mode"] = "Storage auto mode",
            ["Moved to the cloud: {0} ({1})."] = "Cloud par bheja: {0} ({1}).",
            ["Removed {0} old junk file(s): {1} freed."] = "{0} purani bekaar file(s) hatayi: {1} khali.",
            ["Off."] = "Band hai.",
            ["On. Last run {0}: moved {1} idle app(s), free RAM {2} → {3}. Now {4} free."] = "Chalu. Pichhli baar {0}: {1} bekaar pade app(s) hataye, khali RAM {2} → {3}. Abhi {4} khali.",
            ["On. Has not had to act yet: {0} of RAM is free (acts below {1})."] = "Chalu. Abhi zaroorat nahi padi: {0} RAM khali hai ({1} se kam par chalega).",
            ["On. {0} GB free, limit {1} GB: nothing to do."] = "Chalu. {0} GB khali, limit {1} GB: kuch karne ki zaroorat nahi.",
            ["On. {0} GB free: {1} GB short of the {2} GB limit."] = "Chalu. {0} GB khali: {2} GB ki limit se {1} GB kam.",
            ["Last {0}: removed {1} of old junk."] = "Pichhli baar {0}: {1} purana kachra hataya.",
            ["Last {0}: moved {1} to the cloud ({2})."] = "Pichhli baar {0}: {1} cloud par bheja ({2}).",
            ["On. Frees RAM when a game starts. No app is set to close."] = "Chalu. Game shuru hote hi RAM khali karta hai. Band karne ke liye koi app nahi chuna.",
            ["On. When a game starts it frees RAM and closes: {0}."] = "Chalu. Game shuru hote hi RAM khali karta hai aur ye band karta hai: {0}.",
            ["Nothing is closed and nothing is lost. A moved app is a little slow the next time you open it. Apps you used in the last 15 minutes, a running game, Windows and Cloud-SSD are left alone."] = "Koi app band nahi hota, kuch khota nahi. Hataya gaya app agli baar kholne par thoda slow chalega. Pichhle 15 minute me use kiye apps, chalta game, Windows aur Cloud-SSD ko nahi chheda jata.",
            ["RAM IN USE"] = "RAM USE HO RAHI HAI",
            ["RAM FREE"] = "RAM KHALI",
            ["RESERVED BY APPS"] = "APPS NE RESERVE KI",
            ["Page file"] = "Page File (Virtual Memory)",
            ["RAM use (last 5 min)"] = "RAM ka Use (pichhle 5 min)",
            ["Near the top, Windows starts using the page file on the SSD and games stutter."] = "Upar ke paas pahunchne par Windows SSD ki page file use karta hai aur game atakta hai.",
            ["Open Windows page-file settings"] = "Page-File Settings Kholein",
            ["Best setting: 'Automatically manage paging file size for all drives'. A page file stops crashes when RAM is full; it does not make the PC faster."] = "Sabse sahi setting: 'Automatically manage paging file size for all drives'. Page file RAM full hone par crash rokti hai; PC tez nahi karti.",
            ["Apps using the most RAM"] = "Sabse zyada RAM lene wale Apps",
            ["Close"] = "Band Karein",
            ["RAM held by all of an app's processes together (approximate). Close asks the app to close the normal way, like pressing X. Apps without a window of their own have no Close button."] = "Ek app ke saare processes ki RAM milakar (andaza). Band Karein dabane par app normal tareeke se band hota hai, jaise X dabana. Jin apps ki apni window nahi hai un par yeh button nahi hota.",
            ["This page only reads what Windows reports. It never changes a Windows setting and closes an app only when you press Close."] = "Yeh page sirf wahi dikhata hai jo Windows batata hai. Koi Windows setting nahi badalta, aur app tabhi band karta hai jab aap Band Karein dabayein.",

            // PC Memory View: text built while the app runs ({0}, {1}… are filled in after translation)
            ["Windows has no page file. When RAM is full, games and apps will close with an 'out of memory' error. In Windows, turn on 'Automatically manage paging file size for all drives'."] = "Windows me page file nahi hai. RAM full hone par games aur apps 'out of memory' error dekar band ho jayenge. Windows me 'Automatically manage paging file size for all drives' chalu karein.",
            ["Apps have reserved {0} of the {1} Windows can give (RAM + page file). At the limit, games close with an 'out of memory' error. Close apps you do not need."] = "Apps ne {1} me se {0} reserve kar liya hai (RAM + page file). Limit par pahunchte hi games 'out of memory' error dekar band ho jate hain. Jo apps nahi chahiye unhe band karein.",
            ["Apps have reserved {0} of the {1} Windows can give (RAM + page file). At the limit, games close with an 'out of memory' error. Close apps you do not need. The page file has a fixed size; set it back to 'Automatically manage' so Windows can grow it."] = "Apps ne {1} me se {0} reserve kar liya hai (RAM + page file). Limit par pahunchte hi games 'out of memory' error dekar band ho jate hain. Jo apps nahi chahiye unhe band karein. Page file ka size fixed hai; ise wapas 'Automatically manage' par karein taaki Windows ise badha sake.",
            ["RAM is almost full: only {0} of {1} is free. When RAM runs out, Windows uses the page file on the SSD, which is far slower and makes games stutter. Close apps from the list before you play."] = "RAM lagbhag full hai: {1} me se sirf {0} khali hai. RAM khatam hone par Windows SSD ki page file use karta hai, jo bahut slow hai aur game atakta hai. Khelne se pehle list se apps band karein.",
            ["RAM is getting low: {0} of {1} is free ({2}). Close apps you do not need before you play."] = "RAM kam ho rahi hai: {1} me se {0} khali hai ({2}). Khelne se pehle jo apps nahi chahiye unhe band karein.",
            ["Only {0} free on {1}. Windows needs space there to grow the page file. Keep at least {2} free."] = "{1} par sirf {0} khali hai. Page file badhane ke liye Windows ko wahan jagah chahiye. Kam se kam {2} khali rakhein.",
            ["Only {0} free on {1}. Windows needs space there to grow the page file, and the Cloud-SSD cache uses the same drive. Keep at least {2} free."] = "{1} par sirf {0} khali hai. Page file badhane ke liye Windows ko wahan jagah chahiye, aur Cloud-SSD ka cache bhi isi drive par hai. Kam se kam {2} khali rakhein.",
            ["Memory looks healthy: {0} of RAM is free."] = "Memory theek hai: {0} RAM khali hai.",
            ["Windows did not report memory figures."] = "Windows ne memory ki jaankari nahi di.",
            ["Last run {0}: moved {1} idle app(s) out of RAM, free RAM {2} → {3}."] = "Pichhli baar {0}: {1} bekaar pade app RAM se hataye, khali RAM {2} → {3}.",
            ["Still below {0} free: the apps you are using need the RAM. Auto mode tries again every {1} minutes; close an app from the list to free more."] = "Abhi bhi {0} se kam khali hai: jo apps aap use kar rahe ho unhe RAM chahiye. Auto mode har {1} minute me dobara chalta hai; aur RAM khali karne ke liye list se koi app band karein.",
            ["Auto mode is off."] = "Auto mode band hai.",
            ["Watching: {0} of RAM is free, auto mode acts below {1}."] = "Nazar rakh raha hai: {0} RAM khali hai, auto mode {1} se kam par chalega.",
            ["RAM is low ({0} free); auto mode can act in {1} min."] = "RAM kam hai ({0} khali); auto mode {1} min me chal sakta hai.",
            ["RAM is low ({0} free); auto mode is acting now."] = "RAM kam hai ({0} khali); auto mode abhi chal raha hai.",
            ["Auto mode is already running."] = "Auto mode pehle se chal raha hai.",
            ["No idle background app to move: everything open was used in the last few minutes or belongs to Windows."] = "Hatane ke liye koi bekaar pada background app nahi hai: jo bhi khula hai woh abhi-abhi use hua hai ya Windows ka hai.",
            ["Moved {0} idle app(s) out of RAM. Free RAM went from {1} to {2}."] = "{0} bekaar pade app RAM se hataye. Khali RAM {1} se {2} ho gayi.",
            ["Close {0}?\n\nCloud Drive asks it to close the normal way, like pressing X. If it asks you to save your work, answer it — unsaved work can be lost."] = "{0} band karein?\n\nCloud Drive ise normal tareeke se band hone ko kehta hai, jaise X dabana. Agar yeh kaam save karne ko pooche to jawab dein — bina save kiya kaam kho sakta hai.",
            ["{0} has no window Cloud Drive can close. Close it yourself or use Task Manager."] = "{0} ki koi window nahi hai jise Cloud Drive band kar sake. Ise khud band karein ya Task Manager use karein.",
            ["{0} was asked to close but is still running. It may be waiting for you to save."] = "{0} ko band hone ko kaha gaya par yeh abhi bhi chal raha hai. Shayad yeh aapke save karne ka intezaar kar raha hai.",
            ["{0} closed. Free RAM went from {1} to {2}."] = "{0} band ho gaya. Khali RAM {1} se {2} ho gayi.",
            ["Could not open the Windows settings: {0}"] = "Windows settings nahi khul payi: {0}",
            ["{0} in use"] = "{0} use me",
            ["of {0} · {1}"] = "{0} me se · {1}",
            ["of {0} limit (RAM + page file)"] = "limit {0} (RAM + page file)",
            ["None"] = "Nahi hai",
            ["In use"] = "Use me",
            ["Free on {0}"] = "{0} par khali",
            ["{0} · sized by Windows"] = "{0} · Windows khud tay karta hai",
            ["{0} · peak {1}"] = "{0} · sabse zyada {1}",
            ["{0} · shared with the Cloud-SSD cache"] = "{0} · Cloud-SSD cache bhi isi par hai",
            ["Windows and Cloud-SSD ({0} processes) — cannot be closed"] = "Windows aur Cloud-SSD ({0} processes) — band nahi ho sakte",

            // Settings View
            ["Network"] = "Internet / Network Settings",
            ["Prediction engine"] = "Smart Prediction Engine",
            ["Startup and security"] = "Startup & Security Settings",
            ["Restore drives when Cloud Drive starts"] = "App khulte hi Drive auto-connect karein",
            ["Keep drives mounted after closing (default answer)"] = "App band hone par bhi Drive connect rakhein",
            ["Encrypt the rclone config (key protected with Windows DPAPI)"] = "Config secure aur encrypt rakhein",
            ["Advanced · rclone VFS"] = "Advanced · rclone Cache Engine",
            ["Save"] = "Save Karein",
            ["Cancel"] = "Cancel"
        },

        ["हिन्दी (Hindi)"] = new(StringComparer.OrdinalIgnoreCase)
        {
            // Sidebar Navigation
            ["Dashboard"] = "डैशबोर्ड",
            ["Cloud Drives"] = "क्लाउड ड्राइव्स",
            ["Game Library"] = "गेम लाइब्रेरी",
            ["Phone (Mobile Hub)"] = "फ़ोन (मोबाइल हब)",
            ["Mobile Hub"] = "फ़ोन (मोबाइल हब)",
            ["Accelerator"] = "स्पीड बूस्टर",
            ["Smart Cache"] = "स्मार्ट एसएसडी कैश",
            ["Performance"] = "लाइव परफॉरमेंस",
            ["Settings"] = "सेटिंग्स",
            ["Diagnostics"] = "डायग्नोस्टिक्स",
            ["Cloud-to-SSD engine"] = "क्लाउड-टू-एसएसडी इंजन",
            ["Cloud-to-SSD virtual drive engine"] = "क्लाउड-टू-एसएसडी वर्चुअल ड्राइव इंजन",
            ["Restore drives when Cloud-SSD starts"] = "ऐप शुरू होने पर ड्राइव्स रिस्टोर करें",
            ["Mount automatically when Cloud-SSD starts"] = "ऐप शुरू होते ही ड्राइव स्वतः माउंट करें",
            ["Welcome to Cloud-SSD"] = "क्लाउड-एसएसडी में आपका स्वागत है",
            ["Language / भाषा"] = "भाषा (Language)",

            // Cloud Drives View & Multi-Provider
            ["Sign in with Google Drive"] = "गूगल ड्राइव कनेक्ट करें",
            ["Connect Cloud Account"] = "क्लाउड खाता जोड़ें",
            ["Add virtual drive"] = "वर्चुअल ड्राइव जोड़ें",
            ["Virtual drives"] = "वर्चुअल ड्राइव्स",
            ["No drives yet. Connect an account, then add a drive."] = "अभी कोई ड्राइव नहीं है। पहले खाता कनेक्ट करें।",
            ["Connected accounts"] = "जुड़े हुए खाते",
            ["None yet."] = "अभी कोई खाता नहीं है।",
            ["Mount"] = "माउंट करें",
            ["Unmount"] = "अनमाउंट करें",
            ["Open"] = "फ़ोल्डर खोलें",
            ["Refresh listing"] = "रिफ्रेश करें",
            ["Disconnect"] = "डिस्कनेक्ट करें",
            ["Remove"] = "हटाएं",
            ["Cloud providers"] = "समर्थित क्लाउड प्रोवाइडर",
            ["Google Drive"] = "गूगल ड्राइव",
            ["Microsoft OneDrive"] = "माइक्रोसॉफ्ट वनड्राइव",
            ["WebDAV / Nextcloud"] = "वेबडेव / नेक्स्टक्लाउड",
            ["Sign in with Microsoft"] = "माइक्रोसॉफ्ट से साइन इन करें",
            ["Connect WebDAV Storage"] = "वेबडेव स्टोरेज कनेक्ट करें",
            ["Personal OneDrive"] = "पर्सनल वनड्राइव",
            ["Business / School / SharePoint"] = "बिज़नेस / स्कूल / शेयरपॉइंट",
            ["SERVER WEBDAV URL"] = "सर्वर वेबडेव यूआरएल",
            ["SERVICE TYPE / VENDOR"] = "सर्विस प्रकार / वेंडर",
            ["USERNAME"] = "यूज़रनेम",
            ["PASSWORD OR APP TOKEN"] = "पासवर्ड या टोकन",
            ["Read-only access (protects cloud files; no writing or installing on it)"] = "केवल पढ़ने की अनुमति (सुरक्षित क्लाउड फ़ाइलें)",

            // Game Library View
            ["Add game"] = "गेम जोड़ें",
            ["No games added yet. Mount a drive, then add a game folder."] = "अभी कोई गेम नहीं जुड़ा है। पहले ड्राइव कनेक्ट करें।",
            ["PLAY"] = "खेलें ▶",
            ["Optimize & Prepare"] = "ऑप्टिमाइज़ और तैयार करें",
            ["Ready to play"] = "खेलने के लिए तैयार",
            ["Needs preparation"] = "तैयारी की आवश्यकता है",
            ["Running"] = "गेम चालू है",
            ["Cloud Direct"] = "क्लाउड डायरेक्ट",
            ["Hybrid Optimized"] = "हाइब्रिड ऑप्टिमाइज़्ड",
            ["Full Local"] = "फुल लोकल",
            ["Verified on this PC"] = "इस पीसी पर सत्यापित",
            ["No blockers found"] = "सब कुछ ठीक है",
            ["Install locally…"] = "लोकल इंस्टॉल करें…",
            ["Game settings"] = "गेम सेटिंग्स",
            ["Remove from library"] = "लाइब्रेरी से हटाएं",

            // Save Game Backup & Sync
            ["CLOUD SAVE-GAME BACKUP & SYNC"] = "क्लाउड गेम-सेव बैकअप और सिंक",
            ["Automatically back up save games to cloud when game exits"] = "गेम बंद होते ही सेव फ़ाइलों का क्लाउड पर स्वतः बैकअप लें",
            ["SAVE FOLDER LOCATION"] = "सेव फ़ोल्डर का स्थान",
            ["Auto-Detect"] = "स्वतः पता लगाएं",
            ["Browse…"] = "ब्राउज़ करें…",
            ["Backup Saves Now"] = "अभी बैकअप लें",
            ["Restore from Cloud"] = "क्लाउड से पुनर्स्थापित करें",

            // Dashboard View
            ["Get started"] = "शुरू करें",
            ["CONNECTION HEALTH"] = "कनेक्शन स्थिति",
            ["CACHE ON SSD"] = "एसएसडी पर कैश",
            ["CACHE HIT RATE (THIS RUN)"] = "कैश हिट दर",
            ["ENGINE"] = "इंजन स्थिति",
            ["Network throughput (rclone, last 2 min)"] = "नेटवर्क थ्रूपुट (लाइव)",
            ["Storage"] = "स्टोरेज विवरण",
            ["Save settings"] = "सेटिंग्स सेव करें",
            ["Open data folder"] = "डेटा फ़ोल्डर खोलें",

            // Performance View
            ["Transfer rate"] = "ट्रांसफर दर",
            ["Cache hit rate"] = "कैश हिट दर",
            ["Average read wait"] = "औसत प्रतीक्षा समय",
            ["Application reads"] = "एप्लिकेशन रीड्स / सेकंड",
            ["Totals since start"] = "प्रारंभ से कुल डेटा",

            // Settings → Storage: auto cloud switch, moved files, what is using the space
            ["⚡ Move Files to Cloud Drive Now"] = "⚡ फ़ाइलें अभी क्लाउड ड्राइव पर भेजें",
            ["Save Settings"] = "सेटिंग्स सेव करें",
            ["⚡ SMART SPACE SAVER & AUTO CLOUD OFFLOAD"] = "⚡ जगह बचाओ और ऑटो क्लाउड स्विच",
            ["When laptop storage drops below your limit, local files automatically switch to Cloud Drive (Z:)."] = "जब लैपटॉप की जगह आपकी सीमा से कम हो, तो फ़ाइलें अपने आप क्लाउड ड्राइव (Z:) पर चली जाती हैं।",
            ["Enable Auto Cloud Switch on Low Storage"] = "जगह कम होने पर ऑटो क्लाउड स्विच चालू रखें",
            ["Trigger when laptop free space is below:"] = "तब चले जब लैपटॉप में खाली जगह इससे कम हो:",
            ["First delete old temporary files (untouched for 7 days, not in use)"] = "पहले पुरानी अस्थायी फ़ाइलें हटाएँ (7 दिन से अनछुई, उपयोग में नहीं)",
            ["Also move old big files from Downloads (over 100 MB, untouched for 7 days; folders are never moved). A shortcut stays in Downloads."] = "डाउनलोड्स की पुरानी बड़ी फ़ाइलें भी क्लाउड पर भेजें (100 MB से बड़ी, 7 दिन से अनछुई; फ़ोल्डर कभी नहीं जाते)। डाउनलोड्स में शॉर्टकट रह जाता है।",
            ["Also empty Recycle Bin items deleted more than 30 days ago"] = "रीसायकल बिन की 30 दिन से पुरानी चीज़ें भी हटाएँ",
            ["While a game or a phone stream is running, nothing new is cleaned or moved and cloud uploads run slowly."] = "गेम या फ़ोन स्ट्रीम चलते समय कुछ नया साफ़ या स्थानांतरित नहीं होता, और क्लाउड अपलोड धीमा चलता है।",
            ["FILES MOVED FROM DOWNLOADS TO THE CLOUD"] = "डाउनलोड्स से क्लाउड पर गई फ़ाइलें",
            ["Refresh"] = "दोबारा देखें",
            ["Bring back"] = "वापस लाएँ",
            ["What is using the space"] = "जगह कौन ले रहा है",
            ["Shows the biggest folders and files on the Windows drive, so you can see what to delete or move. Nothing is changed."] = "विंडोज़ ड्राइव के सबसे बड़े फ़ोल्डर और फ़ाइलें दिखाता है, ताकि आप देख सकें क्या हटाना या भेजना है। कुछ बदला नहीं जाता।",
            ["Scan now"] = "अभी जाँचें",
            ["{0} GB free: {1} GB short of the {2} GB limit. Auto mode cleans and moves what it can; the list below shows what else takes space."] = "{0} GB खाली: {2} GB की सीमा से {1} GB कम। ऑटो मोड जो साफ़ या स्थानांतरित कर सकता है करता है; नीचे की सूची दिखाती है और क्या जगह ले रहा है।",
            ["✓ Storage Safe: {0} GB free (Threshold: {1} GB)"] = "✓ जगह ठीक है: {0} GB खाली (सीमा: {1} GB)",
            ["Reading the drive… this takes a minute or two."] = "ड्राइव पढ़ रहा है… एक-दो मिनट लगेंगे।",
            ["Biggest folders and files on {0}, read in {1} s. Sizes are what they really take on the disk."] = "{0} के सबसे बड़े फ़ोल्डर और फ़ाइलें, {1} सेकंड में पढ़े। आकार वही है जो डिस्क पर असल में लगता है।",
            ["Could not read the drive: {0}"] = "ड्राइव पढ़ नहीं पाया: {0}",
            ["Could not open the folder: {0}"] = "फ़ोल्डर खुल नहीं पाया: {0}",
            ["No file from Downloads is on the cloud right now."] = "अभी डाउनलोड्स की कोई फ़ाइल क्लाउड पर नहीं है।",
            ["{0} file(s) from Downloads are on the cloud ({1})."] = "डाउनलोड्स की {0} फ़ाइल(ें) क्लाउड पर हैं ({1})।",
            ["Bringing {0} back from the cloud…"] = "{0} को क्लाउड से वापस ला रहा है…",
            ["Windows itself. Leave it."] = "यह विंडोज़ खुद है। इसे न छेड़ें।",
            ["Windows' virtual memory. Leave it."] = "विंडोज़ की वर्चुअल मेमोरी। इसे न छेड़ें।",
            ["Used for sleep / fast start-up."] = "स्लीप / फ़ास्ट स्टार्ट-अप के लिए।",
            ["Recycle Bin. Empty it to get this space back."] = "रीसायकल बिन। खाली करें तो यह जगह वापस मिलेगी।",
            ["Installed programs. Uninstall what you do not use."] = "इंस्टॉल किए प्रोग्राम। जो उपयोग नहीं करते उन्हें अनइंस्टॉल करें।",
            ["Temporary files. Cloud-SSD removes the old ones by itself."] = "अस्थायी फ़ाइलें। पुरानी Cloud-SSD खुद हटा देता है।",
            ["Download cache of a developer tool. Safe to delete; it downloads again."] = "डेवलपर टूल का डाउनलोड कैश। हटाना सुरक्षित है; वह दोबारा डाउनलोड कर लेता है।",
            ["A cache. Usually safe to delete; the app fills it again."] = "यह कैश है। अक्सर हटाना सुरक्षित है; ऐप इसे दोबारा भर लेता है।",
            ["Your downloads."] = "आपके डाउनलोड्स।",

            // PC Memory View
            ["Auto mode: when less than 30% of RAM is free, move idle background apps out of RAM"] = "ऑटो मोड: जब 30% से कम रैम खाली हो, तो बेकार पड़े बैकग्राउंड ऐप्स को रैम से हटा दो",
            ["Run now"] = "अभी चलाएँ",
            ["Game mode: when a game or a phone stream starts, free RAM at once and close the apps chosen here"] = "गेम मोड: गेम या फ़ोन स्ट्रीम शुरू होते ही रैम तुरंत खाली करो और यहाँ चुने ऐप्स बंद करो",
            ["Open those apps again when the game is over"] = "गेम खत्म होने पर उन ऐप्स को वापस खोलो",
            ["Game mode acts for a game started from the Game Library and for an Android emulator in front (BlueStacks and the like). Each app is asked to close the normal way, so one with unsaved work stays open and asks you."] = "गेम मोड तब चलता है जब गेम लाइब्रेरी से गेम शुरू हो या कोई एंड्रॉइड एम्युलेटर (BlueStacks जैसा) सामने हो। हर ऐप सामान्य तरीके से बंद होता है, इसलिए जिसमें बिना सेव किया काम है वह खुला रहता है और आपसे पूछता है।",
            ["Close in games"] = "गेम में बंद करो",
            ["RAM auto mode"] = "रैम ऑटो मोड",
            ["Game mode"] = "गेम मोड",
            ["During the game {0} app(s) were closed and {1} of RAM was freed."] = "गेम के दौरान {0} ऐप बंद किए और {1} रैम खाली की।",
            ["Opened again: {0}."] = "वापस खोले: {0}।",
            ["Game mode is off."] = "गेम मोड बंद है।",
            ["When a game starts, RAM is freed at once. No app is set to close: pick apps with 'Close in games' in the list below."] = "गेम शुरू होते ही रैम तुरंत खाली होती है। बंद करने के लिए कोई ऐप नहीं चुना: नीचे की सूची में 'गेम में बंद करो' से चुनें।",
            ["When a game starts, RAM is freed at once and these apps are closed:"] = "गेम शुरू होते ही रैम तुरंत खाली होती है और ये ऐप्स बंद होते हैं:",
            ["A game is running now."] = "अभी गेम चल रहा है।",
            ["Last game {0}: closed {1} app(s), free RAM {2} → {3}."] = "पिछला गेम {0}: {1} ऐप बंद किए, खाली रैम {2} → {3}।",
            ["Auto modes"] = "ऑटो मोड",
            ["Storage auto mode"] = "स्टोरेज ऑटो मोड",
            ["Moved to the cloud: {0} ({1})."] = "क्लाउड पर भेजा: {0} ({1})।",
            ["Removed {0} old junk file(s): {1} freed."] = "{0} पुरानी बेकार फ़ाइलें हटाईं: {1} खाली।",
            ["Off."] = "बंद है।",
            ["On. Last run {0}: moved {1} idle app(s), free RAM {2} → {3}. Now {4} free."] = "चालू। पिछली बार {0}: {1} बेकार पड़े ऐप हटाए, खाली रैम {2} → {3}। अभी {4} खाली।",
            ["On. Has not had to act yet: {0} of RAM is free (acts below {1})."] = "चालू। अभी ज़रूरत नहीं पड़ी: {0} रैम खाली है ({1} से कम पर चलेगा)।",
            ["On. {0} GB free, limit {1} GB: nothing to do."] = "चालू। {0} GB खाली, सीमा {1} GB: कुछ करने की ज़रूरत नहीं।",
            ["On. {0} GB free: {1} GB short of the {2} GB limit."] = "चालू। {0} GB खाली: {2} GB की सीमा से {1} GB कम।",
            ["Last {0}: removed {1} of old junk."] = "पिछली बार {0}: {1} पुराना कचरा हटाया।",
            ["Last {0}: moved {1} to the cloud ({2})."] = "पिछली बार {0}: {1} क्लाउड पर भेजा ({2})।",
            ["On. Frees RAM when a game starts. No app is set to close."] = "चालू। गेम शुरू होते ही रैम खाली करता है। बंद करने के लिए कोई ऐप नहीं चुना।",
            ["On. When a game starts it frees RAM and closes: {0}."] = "चालू। गेम शुरू होते ही रैम खाली करता है और ये बंद करता है: {0}।",
            ["Nothing is closed and nothing is lost. A moved app is a little slow the next time you open it. Apps you used in the last 15 minutes, a running game, Windows and Cloud-SSD are left alone."] = "कोई ऐप बंद नहीं होता, कुछ खोता नहीं। हटाया गया ऐप अगली बार खोलने पर थोड़ा धीमा चलेगा। पिछले 15 मिनट में इस्तेमाल किए ऐप्स, चलता गेम, विंडोज़ और Cloud-SSD को नहीं छेड़ा जाता।",
            ["RAM IN USE"] = "उपयोग में रैम",
            ["RAM FREE"] = "खाली रैम",
            ["RESERVED BY APPS"] = "ऐप्स द्वारा आरक्षित",
            ["Page file"] = "पेज फ़ाइल (वर्चुअल मेमोरी)",
            ["RAM use (last 5 min)"] = "रैम उपयोग (पिछले 5 मिनट)",
            ["Near the top, Windows starts using the page file on the SSD and games stutter."] = "ऊपर के पास पहुँचने पर विंडोज़ एसएसडी की पेज फ़ाइल इस्तेमाल करता है और गेम अटकता है।",
            ["Open Windows page-file settings"] = "पेज-फ़ाइल सेटिंग्स खोलें",
            ["Best setting: 'Automatically manage paging file size for all drives'. A page file stops crashes when RAM is full; it does not make the PC faster."] = "सबसे सही सेटिंग: 'Automatically manage paging file size for all drives'। पेज फ़ाइल रैम भरने पर क्रैश रोकती है; पीसी तेज़ नहीं करती।",
            ["Apps using the most RAM"] = "सबसे ज़्यादा रैम लेने वाले ऐप्स",
            ["Close"] = "बंद करें",
            ["RAM held by all of an app's processes together (approximate). Close asks the app to close the normal way, like pressing X. Apps without a window of their own have no Close button."] = "एक ऐप के सभी प्रोसेस की रैम मिलाकर (अनुमानित)। बंद करें दबाने पर ऐप सामान्य तरीके से बंद होता है, जैसे X दबाना। जिन ऐप्स की अपनी विंडो नहीं है उन पर यह बटन नहीं होता।",
            ["This page only reads what Windows reports. It never changes a Windows setting and closes an app only when you press Close."] = "यह पेज केवल वही दिखाता है जो विंडोज़ बताता है। कोई विंडोज़ सेटिंग नहीं बदलता, और ऐप तभी बंद करता है जब आप बंद करें दबाएँ।",

            // PC Memory View: text built while the app runs ({0}, {1}… are filled in after translation)
            ["Windows has no page file. When RAM is full, games and apps will close with an 'out of memory' error. In Windows, turn on 'Automatically manage paging file size for all drives'."] = "विंडोज़ में पेज फ़ाइल नहीं है। रैम भरने पर गेम और ऐप्स 'out of memory' एरर देकर बंद हो जाएँगे। विंडोज़ में 'Automatically manage paging file size for all drives' चालू करें।",
            ["Apps have reserved {0} of the {1} Windows can give (RAM + page file). At the limit, games close with an 'out of memory' error. Close apps you do not need."] = "ऐप्स ने {1} में से {0} आरक्षित कर लिया है (रैम + पेज फ़ाइल)। सीमा पर पहुँचते ही गेम 'out of memory' एरर देकर बंद हो जाते हैं। जो ऐप्स नहीं चाहिए उन्हें बंद करें।",
            ["Apps have reserved {0} of the {1} Windows can give (RAM + page file). At the limit, games close with an 'out of memory' error. Close apps you do not need. The page file has a fixed size; set it back to 'Automatically manage' so Windows can grow it."] = "ऐप्स ने {1} में से {0} आरक्षित कर लिया है (रैम + पेज फ़ाइल)। सीमा पर पहुँचते ही गेम 'out of memory' एरर देकर बंद हो जाते हैं। जो ऐप्स नहीं चाहिए उन्हें बंद करें। पेज फ़ाइल का आकार तय (fixed) है; इसे वापस 'Automatically manage' पर करें ताकि विंडोज़ इसे बढ़ा सके।",
            ["RAM is almost full: only {0} of {1} is free. When RAM runs out, Windows uses the page file on the SSD, which is far slower and makes games stutter. Close apps from the list before you play."] = "रैम लगभग भर गई है: {1} में से केवल {0} खाली है। रैम खत्म होने पर विंडोज़ एसएसडी की पेज फ़ाइल इस्तेमाल करता है, जो बहुत धीमी है और गेम अटकता है। खेलने से पहले सूची से ऐप्स बंद करें।",
            ["RAM is getting low: {0} of {1} is free ({2}). Close apps you do not need before you play."] = "रैम कम हो रही है: {1} में से {0} खाली है ({2})। खेलने से पहले जो ऐप्स नहीं चाहिए उन्हें बंद करें।",
            ["Only {0} free on {1}. Windows needs space there to grow the page file. Keep at least {2} free."] = "{1} पर केवल {0} खाली है। पेज फ़ाइल बढ़ाने के लिए विंडोज़ को वहाँ जगह चाहिए। कम से कम {2} खाली रखें।",
            ["Only {0} free on {1}. Windows needs space there to grow the page file, and the Cloud-SSD cache uses the same drive. Keep at least {2} free."] = "{1} पर केवल {0} खाली है। पेज फ़ाइल बढ़ाने के लिए विंडोज़ को वहाँ जगह चाहिए, और Cloud-SSD का कैश भी इसी ड्राइव पर है। कम से कम {2} खाली रखें।",
            ["Memory looks healthy: {0} of RAM is free."] = "मेमोरी ठीक है: {0} रैम खाली है।",
            ["Windows did not report memory figures."] = "विंडोज़ ने मेमोरी की जानकारी नहीं दी।",
            ["Last run {0}: moved {1} idle app(s) out of RAM, free RAM {2} → {3}."] = "पिछली बार {0}: {1} बेकार पड़े ऐप रैम से हटाए, खाली रैम {2} → {3}।",
            ["Still below {0} free: the apps you are using need the RAM. Auto mode tries again every {1} minutes; close an app from the list to free more."] = "अभी भी {0} से कम खाली है: जो ऐप्स आप इस्तेमाल कर रहे हैं उन्हें रैम चाहिए। ऑटो मोड हर {1} मिनट में दोबारा चलता है; और रैम खाली करने के लिए सूची से कोई ऐप बंद करें।",
            ["Auto mode is off."] = "ऑटो मोड बंद है।",
            ["Watching: {0} of RAM is free, auto mode acts below {1}."] = "नज़र रख रहा है: {0} रैम खाली है, ऑटो मोड {1} से कम पर चलेगा।",
            ["RAM is low ({0} free); auto mode can act in {1} min."] = "रैम कम है ({0} खाली); ऑटो मोड {1} मिनट में चल सकता है।",
            ["RAM is low ({0} free); auto mode is acting now."] = "रैम कम है ({0} खाली); ऑटो मोड अभी चल रहा है।",
            ["Auto mode is already running."] = "ऑटो मोड पहले से चल रहा है।",
            ["No idle background app to move: everything open was used in the last few minutes or belongs to Windows."] = "हटाने के लिए कोई बेकार पड़ा बैकग्राउंड ऐप नहीं है: जो भी खुला है वह अभी-अभी इस्तेमाल हुआ है या विंडोज़ का है।",
            ["Moved {0} idle app(s) out of RAM. Free RAM went from {1} to {2}."] = "{0} बेकार पड़े ऐप रैम से हटाए। खाली रैम {1} से {2} हो गई।",
            ["Close {0}?\n\nCloud Drive asks it to close the normal way, like pressing X. If it asks you to save your work, answer it — unsaved work can be lost."] = "{0} बंद करें?\n\nCloud Drive इसे सामान्य तरीके से बंद होने को कहता है, जैसे X दबाना। अगर यह काम सेव करने को पूछे तो जवाब दें — बिना सेव किया काम खो सकता है।",
            ["{0} has no window Cloud Drive can close. Close it yourself or use Task Manager."] = "{0} की कोई विंडो नहीं है जिसे Cloud Drive बंद कर सके। इसे खुद बंद करें या Task Manager इस्तेमाल करें।",
            ["{0} was asked to close but is still running. It may be waiting for you to save."] = "{0} को बंद होने को कहा गया पर यह अभी भी चल रहा है। शायद यह आपके सेव करने का इंतज़ार कर रहा है।",
            ["{0} closed. Free RAM went from {1} to {2}."] = "{0} बंद हो गया। खाली रैम {1} से {2} हो गई।",
            ["Could not open the Windows settings: {0}"] = "विंडोज़ सेटिंग्स नहीं खुल पाईं: {0}",
            ["{0} in use"] = "{0} उपयोग में",
            ["of {0} · {1}"] = "{0} में से · {1}",
            ["of {0} limit (RAM + page file)"] = "सीमा {0} (रैम + पेज फ़ाइल)",
            ["None"] = "नहीं है",
            ["File"] = "फ़ाइल",
            ["Size"] = "आकार",
            ["In use"] = "उपयोग में",
            ["Free on {0}"] = "{0} पर खाली",
            ["{0} · sized by Windows"] = "{0} · विंडोज़ खुद तय करता है",
            ["{0} · fixed size"] = "{0} · तय आकार",
            ["{0} · peak {1}"] = "{0} · सबसे ज़्यादा {1}",
            ["{0} · shared with the Cloud-SSD cache"] = "{0} · Cloud-SSD कैश भी इसी पर है",
            ["{0} ({1} processes)"] = "{0} ({1} प्रोसेस)",
            ["Windows and Cloud-SSD ({0} processes) — cannot be closed"] = "विंडोज़ और Cloud-SSD ({0} प्रोसेस) — बंद नहीं हो सकते",

            // Settings View
            ["Network"] = "नेटवर्क सेटिंग्स",
            ["Prediction engine"] = "प्रेडिक्शन इंजन",
            ["Startup and security"] = "स्टार्टअप और सुरक्षा",
            ["Restore drives when Cloud Drive starts"] = "ऐप शुरू होने पर ड्राइव्स रिस्टोर करें",
            ["Keep drives mounted after closing (default answer)"] = "ऐप बंद होने के बाद भी ड्राइव्स कनेक्ट रखें",
            ["Encrypt the rclone config (key protected with Windows DPAPI)"] = "आरक्लोन कॉन्फिग को एन्क्रिप्ट करें",
            ["Advanced · rclone VFS"] = "उन्नत · आरक्लोन वीएफएस",
            ["Save"] = "सेव करें",
            ["Cancel"] = "रद्द करें"
        },

        ["Español (Spanish)"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Dashboard"] = "Panel de control",
            ["Cloud Drives"] = "Unidades en la nube",
            ["Game Library"] = "Biblioteca de juegos",
            ["Accelerator"] = "Acelerador",
            ["Smart Cache"] = "Caché inteligente",
            ["Performance"] = "Rendimiento en vivo",
            ["Settings"] = "Configuración",
            ["Diagnostics"] = "Diagnósticos",
            ["Sign in with Google Drive"] = "Iniciar sesión con Google Drive",
            ["Connect Cloud Account"] = "Conectar cuenta en la nube",
            ["Add virtual drive"] = "Agregar unidad virtual",
            ["Virtual drives"] = "Unidades virtuales",
            ["Connected accounts"] = "Cuentas conectadas",
            ["Mount"] = "Montar",
            ["Unmount"] = "Desmontar",
            ["Open"] = "Abrir",
            ["Save settings"] = "Guardar configuración",
            ["Transfer rate"] = "Velocidad de transferencia",
            ["Add game"] = "Añadir juego",
            ["PLAY"] = "JUGAR ▶",
            ["Save"] = "Guardar",
            ["Cancel"] = "Cancelar"
        },

        ["Français (French)"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Dashboard"] = "Tableau de bord",
            ["Cloud Drives"] = "Disques Cloud",
            ["Game Library"] = "Bibliothèque de jeux",
            ["Accelerator"] = "Accélérateur",
            ["Smart Cache"] = "Cache intelligent",
            ["Performance"] = "Performance en direct",
            ["Settings"] = "Paramètres",
            ["Diagnostics"] = "Diagnostics",
            ["Sign in with Google Drive"] = "Se connecter à Google Drive",
            ["Connect Cloud Account"] = "Connecter un compte Cloud",
            ["Add virtual drive"] = "Ajouter un disque virtuel",
            ["Virtual drives"] = "Disques virtuels",
            ["Connected accounts"] = "Comptes connectés",
            ["Mount"] = "Monter",
            ["Unmount"] = "Démonter",
            ["Open"] = "Ouvrir",
            ["Save settings"] = "Enregistrer les paramètres",
            ["Transfer rate"] = "Vitesse de transfert",
            ["Add game"] = "Ajouter un jeu",
            ["PLAY"] = "JOUER ▶",
            ["Save"] = "Enregistrer",
            ["Cancel"] = "Annuler"
        },

        ["Deutsch (German)"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Dashboard"] = "Übersicht",
            ["Cloud Drives"] = "Cloud-Laufwerke",
            ["Game Library"] = "Spielebibliothek",
            ["Accelerator"] = "Beschleuniger",
            ["Smart Cache"] = "Intelligenter Cache",
            ["Performance"] = "Live-Leistung",
            ["Settings"] = "Einstellungen",
            ["Diagnostics"] = "Diagnose",
            ["Sign in with Google Drive"] = "Mit Google Drive anmelden",
            ["Connect Cloud Account"] = "Cloud-Konto verbinden",
            ["Add virtual drive"] = "Virtuelles Laufwerk hinzufügen",
            ["Virtual drives"] = "Virtuelle Laufwerke",
            ["Connected accounts"] = "Verbundene Konten",
            ["Mount"] = "Einbinden",
            ["Unmount"] = "Trennen",
            ["Open"] = "Öffnen",
            ["Save settings"] = "Einstellungen speichern",
            ["Transfer rate"] = "Übertragungsrate",
            ["Add game"] = "Spiel hinzufügen",
            ["PLAY"] = "SPIELEN ▶",
            ["Save"] = "Speichern",
            ["Cancel"] = "Abbrechen"
        },

        ["日本語 (Japanese)"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Dashboard"] = "ダッシュボード",
            ["Cloud Drives"] = "クラウドドライブ",
            ["Game Library"] = "ゲームライブラリ",
            ["Accelerator"] = "アクセラレータ",
            ["Smart Cache"] = "スマートキャッシュ",
            ["Performance"] = "パフォーマンス",
            ["Settings"] = "設定",
            ["Diagnostics"] = "診断",
            ["Sign in with Google Drive"] = "Googleドライブに接続",
            ["Connect Cloud Account"] = "クラウドアカウントを接続",
            ["Add virtual drive"] = "仮想ドライブを追加",
            ["Virtual drives"] = "仮想ドライブ",
            ["Connected accounts"] = "接続済みアカウント",
            ["Mount"] = "マウント",
            ["Unmount"] = "アンマウント",
            ["Open"] = "開く",
            ["Save settings"] = "設定を保存",
            ["Transfer rate"] = "転送速度",
            ["Add game"] = "ゲームを追加",
            ["PLAY"] = "プレイ ▶",
            ["Save"] = "保存",
            ["Cancel"] = "キャンセル"
        }
    };
}
