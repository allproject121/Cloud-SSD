using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Data;

namespace CloudPlayX.Desktop;

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
                    if (orig == null)
                    {
                        orig = tb.Text;
                        tb.SetValue(OriginalTextProperty, orig);
                    }
                    if (!string.IsNullOrEmpty(orig))
                    {
                        var trans = T(orig);
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

            // PC Memory View
            ["RAM IN USE"] = "RAM USE HO RAHI HAI",
            ["RAM FREE"] = "RAM KHALI",
            ["RESERVED BY APPS (COMMIT)"] = "APPS NE RESERVE KI (COMMIT)",
            ["Page file"] = "Page File (Virtual Memory)",
            ["RAM use (last 5 min)"] = "RAM ka Use (pichhle 5 min)",
            ["Near the top, Windows starts using the page file on the SSD and games stutter."] = "Upar ke paas pahunchne par Windows SSD ki page file use karta hai aur game atakta hai.",
            ["Open Windows page-file settings"] = "Windows ki Page-File Settings Kholein",
            ["Best setting: 'Automatically manage paging file size for all drives'. A page file stops crashes when RAM is full; it does not make the PC faster."] = "Sabse sahi setting: 'Automatically manage paging file size for all drives'. Page file RAM full hone par crash rokti hai; PC tez nahi karti.",
            ["Apps using the most RAM"] = "Sabse zyada RAM lene wale Apps",
            ["Close"] = "Band Karein",
            ["RAM held by all of an app's processes together (approximate). Close asks the app to close the normal way, like pressing X; Windows and Cloud-SSD processes have no Close button."] = "Ek app ke saare processes ki RAM milakar (andaza). Band Karein dabane par app normal tareeke se band hota hai, jaise X dabana; Windows aur Cloud-SSD ke process par yeh button nahi hota.",
            ["This page only reads what Windows reports. It never changes a Windows setting and closes an app only when you press Close."] = "Yeh page sirf wahi dikhata hai jo Windows batata hai. Koi Windows setting nahi badalta, aur app tabhi band karta hai jab aap Band Karein dabayein.",

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

            // PC Memory View
            ["RAM IN USE"] = "उपयोग में रैम",
            ["RAM FREE"] = "खाली रैम",
            ["RESERVED BY APPS (COMMIT)"] = "ऐप्स द्वारा आरक्षित (कमिट)",
            ["Page file"] = "पेज फ़ाइल (वर्चुअल मेमोरी)",
            ["RAM use (last 5 min)"] = "रैम उपयोग (पिछले 5 मिनट)",
            ["Near the top, Windows starts using the page file on the SSD and games stutter."] = "ऊपर के पास पहुँचने पर विंडोज़ एसएसडी की पेज फ़ाइल इस्तेमाल करता है और गेम अटकता है।",
            ["Open Windows page-file settings"] = "विंडोज़ की पेज-फ़ाइल सेटिंग्स खोलें",
            ["Best setting: 'Automatically manage paging file size for all drives'. A page file stops crashes when RAM is full; it does not make the PC faster."] = "सबसे सही सेटिंग: 'Automatically manage paging file size for all drives'। पेज फ़ाइल रैम भरने पर क्रैश रोकती है; पीसी तेज़ नहीं करती।",
            ["Apps using the most RAM"] = "सबसे ज़्यादा रैम लेने वाले ऐप्स",
            ["Close"] = "बंद करें",
            ["RAM held by all of an app's processes together (approximate). Close asks the app to close the normal way, like pressing X; Windows and Cloud-SSD processes have no Close button."] = "एक ऐप के सभी प्रोसेस की रैम मिलाकर (अनुमानित)। बंद करें दबाने पर ऐप सामान्य तरीके से बंद होता है, जैसे X दबाना; विंडोज़ और Cloud-SSD के प्रोसेस पर यह बटन नहीं होता।",
            ["This page only reads what Windows reports. It never changes a Windows setting and closes an app only when you press Close."] = "यह पेज केवल वही दिखाता है जो विंडोज़ बताता है। कोई विंडोज़ सेटिंग नहीं बदलता, और ऐप तभी बंद करता है जब आप बंद करें दबाएँ।",

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
