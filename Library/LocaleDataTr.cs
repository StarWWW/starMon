// StarMon: hardware monitoring and control
// Portions copyright © 2023-2024 Piotr Szczepański (GPL-3.0)

using System;
using System.Collections.Generic;
using StarMon.Library;

namespace StarMon.Library.Locale
{

    // Built-in Turkish messages, the counterpart of msgFallback in LocaleData.cs.
    //
    // Every key defined there is defined here too, with the deliberate exception
    // of _ConfigXmlTemplate: the configuration file it generates is read back by
    // the parser and its comments are the same for every language, so it is left
    // to fall back. A missing key is not an error, it simply falls back to the
    // English text, but keeping the two in step means the interface never mixes
    // the two languages. To verify after editing either file:
    //
    //   grep -oP '^\s*\["\K[A-Za-z_0-9]+(?="\])' <file> | sort -u
    //
    // and compare the two listings with comm.
    public abstract partial class LocaleData
    {

        // Turkish message data
        protected Dictionary<string, string> msgTurkish
            = new Dictionary<string, string>()
            {

                // CLI
                ["CliHeader"] = "Donanım İzleme ve Denetim Aracı",
                ["CliHeaderVersion"] = "Sürüm",
                ["CliActionGet"] = "-",
                ["CliActionSet"] = "+",
                ["CliDetailsFollow"] = Conv.GetChar(Conv.SpecialChar.ArrowDown),
                ["CliStateOn"] = "Evet",
                ["CliStateOff"] = "Hayır",
                ["CliTranslated"] = "Türkçeye çeviren: Star",

                // CLI: BIOS
                ["CliBios"] = "BIOS",
                ["CliBiosAdapter"] = "Akıllı Güç Adaptörü Durumu",
                ["CliBiosAnim"] = "LED Animasyon Tablosu",
                ["CliBiosBacklight"] = "Klavye Arka Aydınlatması",
                ["CliBiosBornDate"] = "Üretim Tarihi",
                ["CliBiosBornDateNote"] = "YYYYAAGG",
                ["CliBiosColor"] = "Klavye Arka Aydınlatma Renk Tablosu",
                ["CliBiosColorZones"] = "Bölgeler",
                ["CliBiosCpuPowerLimit1"] = "CPU Güç Sınırı 1",
                ["CliBiosCpuPowerLimit4"] = "CPU Güç Sınırı 4",
                ["CliBiosCpuPowerLimitWithGpu"] = "GPU ile Eşzamanlı CPU Güç Sınırı",
                ["CliBiosFanCount"] = "Fan Sayısı",
                ["CliBiosFanLevelN"] = "Fan #{0} Seviyesi",
                ["CliBiosFanMax"] = "Azami Fan Hızı",
                ["CliBiosFanMode"] = "Fan Modu",
                ["CliBiosFanTable"] = "Fan Hız Seviyesi Tablosu",
                ["CliBiosFanTableFans"] = "Fanlar",
                ["CliBiosFanTableLevels"] = "Seviyeler",
                ["CliBiosFanType"] = "Fan Türü",
                ["CliBiosFanTypeN"] = "Fan #{0} Türü",
                ["CliBiosGpuMode"] = "Grafik Modu (Eski)",
                ["CliBiosGpuPower"] = "GPU Güç Ayarları",
                ["CliBiosGpuPowerCustomTgp"] = "GPU Özel Toplam Grafik Gücü (cTGP)",
                ["CliBiosGpuPowerDState"] = "GPU Aygıt Güç Durumu (DState)",
                ["CliBiosGpuPowerPeakTemperature"] = "GPU Tepe Sıcaklık Sensörü Eşiği",
                ["CliBiosGpuPowerPpab"] = "GPU İşlem Başarımı Yapay Zekâ Desteği (PPAB)",
                ["CliBiosHasBacklight"] = "Klavye Arka Aydınlatma Desteği",
                ["CliBiosHasMemoryOverclock"] = "Bellek Hız Aşırtma Desteği",
                ["CliBiosHasOverclock"] = "Hız Aşırtma Desteği",
                ["CliBiosHasUndervolt"] = "BIOS Voltaj Düşürme Desteği",
                ["CliBiosIdle"] = "Boşta Modu",
                ["CliBiosKbdType"] = "Klavye Türü",
                ["CliBiosSystem"] = "Sistem Tasarım Verisi",
                ["CliBiosSystemBiosOc"] = "BIOS Tanımlı Hız Aşırtma",
                ["CliBiosSystemDefaultCpuPowerLimit4"] = "Varsayılan CPU Güç Sınırı 4",
                ["CliBiosSystemDefaultCpuPowerLimitWithGpu"] = "Varsayılan GPU ile Eşzamanlı CPU Güç Sınırı",
                ["CliBiosSystemDefaultCpuPowerLimitWithGpuNote"] = "Cybug 23C1 ve Sonrası",
                ["CliBiosSystemGpuModeSwitch"] = "Grafik Modu Değiştirme Desteği",
                ["CliBiosSystemStatusFlags"] = "Durum Bayrakları",
                ["CliBiosSystemSupportFlags"] = "Destek Bayrakları",
                ["CliBiosSystemThermalPolicyVersion"] = "Termal İlke Sürümü",
                ["CliBiosSystemUnknown2"] = "Bilinmeyen Bayt",
                ["CliBiosSystemUnknown2Note"] = "Gözlemlenen Sabit 0x35 = 53",
                ["CliBiosTemp"] = "Sıcaklık",
                ["CliBiosThrottling"] = "Termal Kısıtlama Durumu",
                ["CliBiosXmp"] = "Bellek XMP Profili",

                // CLI: Embedded Controller
                ["CliEc"] = "Gömülü Denetleyici",
                ["CliProbe"] = "Donanım Raporu",
                ["CliEcMon"] = "Gömülü Denetleyici İzleyicisi",
                ["CliEcByte"] = "Bayt",
                ["CliEcRegister"] = "Yazmaç",
                ["CliEcWord"] = "Sözcük",
                ["CliEcWordNote"] = "(Küçük Endian)",

                // CLI: Program
                ["CliProg"] = "Program",
                ["CliProgCallback"] = "Geri Çağrı",
                ["CliProgName"] = "Program",
                ["CliProgFanMode"] = "Fan Modu",
                ["CliProgGpuPower"] = "GPU Gücü",

                // CLI: Task
                ["CliTask"] = "Görev Zamanlama",
                ["CliTaskGui"] = "Kullanıcı Oturum Açtığında Otomatik Başlat",
                ["CliTaskKey"] = "Omen Tuşu Yakalama",
                ["CliTaskMux"] = "Advanced Optimus Hata Düzeltmesi",

                // CLI: Usage
                ["CliUsage"] = "Kullanım Bilgisi",
                ["CliUsageText"] =
                    "Kullanım: {0} [-<Arg1> [...] [-<ArgN> [...]]]" + Environment.NewLine +
                    "Burada:" + Environment.NewLine +
                    "<Arg#>" + Environment.NewLine +
                    "  -Bios                     Yalnızca bilgi alan tüm BIOS işlemlerini çalıştır" + Environment.NewLine +
                    "  -Bios <BiosOp>[=<Data>]+  Bir veya daha fazla BIOS işlemini isteğe bağlı parametrelerle gerçekleştir" + Environment.NewLine +
                    "  -Ec                       Tüm Gömülü Denetleyici yazmaçlarının değerini tablo biçiminde göster" + Environment.NewLine +
                    "  -Ec [<Reg>][=<Byte>]+     Belirli yazmaçların bayt değerlerini oku veya yaz" + Environment.NewLine +
                    "  -Ec [<Reg>(2)][=<Word>]+  Ardışık yazmaç çiftlerinin sözcük değerlerini oku veya yaz" + Environment.NewLine +
                    "  -EcMon [DosyaAdı]         Tüm yazmaçları değişiklik için izle ve bildir, isteğe bağlı olarak dosyaya kaydet" + Environment.NewLine +
                    "  -Prog                     Yapılandırma dosyasından yüklenen fan denetim programlarını listele" + Environment.NewLine +
                    "  -Prog <Ad>                Belirtilen fan denetim programını çalıştır" + Environment.NewLine +
                    "  -Run <TName> [<Args>]     Belirtilen görevi çalıştır (arayüzsüz kipte, konsol çıktısı olmadan)" + Environment.NewLine +
                    "  -Task                     Tüm zamanlanmış görevlerin durumunu denetle" + Environment.NewLine +
                    "  -Task <TName>[=<Flag>]+   Zamanlanmış bir görevi etkinleştir veya devre dışı bırak" + Environment.NewLine +
                    "  -SelfTest                 Gömülü testleri çalıştır (donanıma dokunmaz)" + Environment.NewLine +
                    "  -?|-H|[-]-Help|[-]-Usage  Kullanım bilgisini göster" + Environment.NewLine +
                    "<BiosOp>" + Environment.NewLine +
                    "  Cpu:PL1=<Byte> Cpu:PL4=<Byte> Cpu:PLGpu=<Byte> Gpu[=<GpuPreset>] GpuMode[=<GpuMode>] Xmp=<Flag>" + Environment.NewLine +
                    "  FanCount FanLevel[=<FanLevel>] FanMax[=<Flag>] FanMode=<FanMode> FanTable[=<FanTable>] FanType" + Environment.NewLine +
                    "  Idle[=<Flag>] Temp Throttling BornDate System Adapter HasOverclock HasMemoryOverclock HasUndervolt" + Environment.NewLine +
                    "  KbdType HasBacklight Backlight[=<Flag>] Color[=<Color>] Anim[=<ByteArray>]" + Environment.NewLine +
                    "<Data>" + Environment.NewLine +
                    "{1}" +
                    "Argümanlar büyük/küçük harfe duyarsızdır. Her argüman istenildiği kadar tekrarlanabilir.",

                // GUI
                ["GuiAlreadyRunning"] = "Zaten arka planda çalışıyor: bildirim alanı simgesine tıklayın veya komut satırı parametreleri için StarMon -Usage çalıştırın",
                ["GuiBtnDel"] = Conv.GetChar(Conv.SpecialChar.HeavyMultiplication),
                ["GuiBtnSet"] = Conv.GetChar(Conv.SpecialChar.HeavyCheckmark),
                ["GuiPromptReboot"] = "Değişikliğin etkili olması için\r\nsistemin yeniden başlatılması gerekiyor\r\n\r\nŞimdi yeniden başlatılsın mı?",
                ["GuiTranslated"] = "Türkçeye çeviren: Star",

                // GUI: About (doubles as an error form)
                ["GuiAboutTitle"] = "StarMon Hakkında",
                ["GuiAboutTitleError"] = "StarMon Hatası",
                ["GuiAboutCaption"] = "Donanım İzleme ve Denetim",
                ["GuiAboutText"] = "{\\rtf1\\ansi WMI BIOS ve Gömülü Denetleyici üzerinden sıcaklıkları izleyin ve fan hızlarını denetleyin. Hafiftir, arka planda çok az kaynak kullanarak çalışır. Star tarafından geliştirilmiştir. GPL-3.0 altında lisanslanan, © 2023-2024 Piotr Szczepański kodunu içerir.}",
                ["GuiAboutTextErrorPrefix"] = "{\\rtf1\\ansi\\deff0{\\colortbl;\\red255\\green0\\blue0;}\\cf1",
                ["GuiAboutTextErrorSuffix"] = "}",

                // GUI: Main
                ["GuiMainFan"] = "Fan İzleme ve Denetim",
                ["GuiMainFan0"] = "CPU",
                ["GuiMainFan1"] = "GPU",
                ["GuiMainFanAuto"] = "Oto",
                ["GuiMainFanConst"] = "Sabit",
                ["GuiMainFanMax"] = "Azami",
                ["GuiMainFanProg"] = "Prog",
                ["GuiMainFanProgSet"] = "Fan Programını Ayarla",
                ["GuiMainFanProgSetNoSel"] = "Program seçilmedi",
                ["GuiMainFanOff"] = "Kapalı",
                ["GuiMainKbd"] = "Klavye Aydınlatması ve Rengi",
                ["GuiMainKbdColorPickLeft"] = "Sol Bölge Rengi",
                ["GuiMainKbdColorPickMiddle"] = "Orta Bölge Rengi",
                ["GuiMainKbdColorPickRight"] = "Sağ Bölge Rengi",
                ["GuiMainKbdColorPickWasd"] = "WASD Tuşları Rengi",
                ["GuiMainKbdColorPickKeyboard"] = "Klavye Rengi",
                ["GuiMainKbdColorPresetAdd"] = "Hazır Ayarı Kaydet",
                ["GuiMainKbdColorPresetAddValueDefault"] = "Yeni Hazır Ayar",
                ["GuiMainKbdColorPresetDel"] = "Hazır Ayarı Sil",
                ["GuiMainKbdColorPresetDelConfirm"] = "Emin misiniz?",
                ["GuiMainKbdColorPresetDelNoSel"] = "Hazır ayar seçilmedi",
                ["GuiMainKbdColorPresetDelPrompt"] = "Sil",
                // GUI: Main, unsupported-feature panel
                ["GuiMainKbdUnsupported"] = "Desteklenmeyen Özellikler",
                ["GuiMainKbdUnsupportedWait"] = "Bu aygıtın hangi özellikleri desteklemediği belirleniyor…",
                ["GuiMainKbdUnsupportedNone"] = "Bu aygıt uygulamadaki tüm özellikleri destekliyor.",
                ["GuiMainKbdUnsupportedList"] = "Aşağıdakiler bu aygıtta desteklenmiyor (arayüzde gizlendi):",
                ["GuiMainKbdUnsupportedFail"] = "Liste oluşturulamadı:",

                ["GuiMainSys"] = "Sistem Durumu ve Bilgisi",
                ["GuiMainSysAdapterNotSupported"] = Conv.RTF_CF1 + "AC Bilinmiyor",
                ["GuiMainSysAdapterMeetsRequirement"] = Conv.RTF_CF3 + "AC Güç Uygun",
                ["GuiMainSysAdapterBelowRequirement"] = Conv.RTF_CF4 + "AC Güç Düşük",
                ["GuiMainSysAdapterBatteryPower"] = Conv.RTF_CF1 + "AC Güç Yok",
                ["GuiMainSysAdapterNotFunctioning"] = Conv.RTF_CF4 + "AC Arıza",
                ["GuiMainSysAdapterError"] = Conv.RTF_CF4 + "AC Hata",
                ["GuiMainSysBorn"] = "*",
                ["GuiMainSysGpu"] = "GPU",
                ["GuiMainSysGpuPpab"] = "PPAB",
                ["GuiMainSysGpuCustomTgp"] = "cTGP",
                ["GuiMainSysGpuDState"] = "DState",
                ["GuiMainSysThrottlingUnknown"] = Conv.RTF_CF1 + "",
                ["GuiMainSysThrottlingDefault"] = Conv.RTF_CF5 + "Kısıtlama Yok",
                ["GuiMainSysThrottlingOn"] = Conv.RTF_CF4 + "Kısıtlanıyor",
                ["GuiMainSysMsgWelcome"] = "Hoş geldiniz!",
                ["GuiMainTitle"] = "StarMon — Donanım İzleme ve Denetim",
                ["GuiMainTmp"] = "Sıcaklık Sensörü Okumaları",
                ["GuiMainTmpCPUT"] = "CPUT",
                ["GuiMainTmpGPTM"] = "GPTM",
                ["GuiMainTmpIRSN"] = "IRSN",
                ["GuiMainTmpRTMP"] = "RTMP",
                ["GuiMainTmpTMP1"] = "TMP1",
                ["GuiMainTmpTNT2"] = "TNT2",
                ["GuiMainTmpTNT3"] = "TNT3",
                ["GuiMainTmpTNT4"] = "TNT4",
                ["GuiMainTmpTNT5"] = "TNT5",

                // GUI: Menu
                ["GuiMenuSubFan"] = "Fan",
                ["GuiMenuActFanMax"] = "Azami",
                ["GuiMenuActFanModeDefault"] = "Varsayılan",
                ["GuiMenuActFanModePerformance"] = "Başarım",
                ["GuiMenuActFanModeCool"] = "Serin",
                ["GuiMenuActFanModeQuiet"] = "Sessiz",
                ["GuiMenuActFanModeExtreme"] = "Uç",
                ["GuiMenuActFanOff"] = "Kapalı",
                ["GuiMenuSubGpu"] = "Ekran kartı",
                ["GuiMenuActGpuDisplayColor"] = "Renk profilini yeniden yükle",
                ["GuiMenuActGpuDisplayOff"] = "Ekranı kapat",
                ["GuiMenuActGpuPowerMin"] = "Temel Güç",
                ["GuiMenuActGpuPowerMed"] = "Ek Güç",
                ["GuiMenuActGpuPowerMax"] = "Destekli Ek Güç",
                ["GuiMenuActGpuRefreshHigh"] = "Yüksek Yenileme Hızı",
                ["GuiMenuActGpuRefreshLow"] = "Standart Yenileme Hızı",
                ["GuiMenuActGpuModeDiscrete"] = "Yalnızca Harici Ekran Kartı",
                ["GuiMenuActGpuModeOptimus"] = "Optimus Yazılımsal Geçiş",
                ["GuiMenuSubKbd"] = "Klavye",
                ["GuiMenuActKbdBacklight"] = "Arka ışık",
                ["GuiMenuActKbdColorPresetDefaultRed"] = "Omen Kırmızı",
                ["GuiMenuActKbdColorPresetDefaultWhite"] = "Omen Beyaz",
                ["GuiMenuSubSet"] = "Ayarlar",
                ["GuiMenuActSetStayTop"] = "Her zaman üstte",
                ["GuiMenuActSetIconDyn"] = "Dinamik Simge",
                ["GuiMenuActSetIconDynBg"] = "Dinamik Arka Plan",
                ["GuiMenuActSetTaskGui"] = "Windows ile Başlat",
                ["GuiMenuActSetAutoconfig"] = "Başlangıçta ayarları uygula",
                ["GuiMenuActSetTaskKey"] = "Omen Tuşunu Yakala",
                ["GuiMenuActSetTaskMux"] = "Advanced Optimus Düzeltmesi",
                ["GuiMenuActFanCurve"] = "Fan eğrisi…",
                ["GuiMenuActGpuBrightness"] = "Parlaklık",
                ["GuiMenuActKbdTempColor"] = "Sıcaklığa tepkili renk",
                ["GuiMenuActKbdFxCycle"] = "Renk döngüsü",
                ["GuiMenuActKbdFxBreathe"] = "Nefes efekti",
                ["GuiMenuActSetThermal"] = "Otomatik ısıl koruma",
                ["GuiMenuActSetThermalLevel"] = "Termal sınır",
                ["GuiMenuActSetThrottleNotify"] = "Isıl kısıtlama bildirimi",
                ["GuiMenuActSetGpuBattery"] = "Pilde GPU'yu yokla",
                ["GuiMenuActSetFanKeepAlive"] = "Elle ayarlanan fan hızını koru",
                ["GuiMenuActSetUpdateInterval"] = "Güncelleme",
                ["GuiMenuActSetPowerMode"] = "Güç modu",
                ["GuiMenuActSetCpuBoost"] = "CPU Hızlandırma",
                ["GuiMenuActSetRefreshPower"] = "Yenileme hızı güç kaynağını izlesin",
                ["GuiMenuActSetCapabilities"] = "Donanım yetenekleri…",
                ["GuiMenuActSetLanguage"] = "Dil",
                ["GuiMenuActSetLanguageAuto"] = "Otomatik",
                ["GuiMenuActSetLanguageEnglish"] = "English",
                ["GuiMenuActSetLanguageTurkish"] = "Türkçe",
                ["GuiMenuActToggleFormLog"] = "Günlük görüntüleyici",
                ["GuiMenuActToggleFormMain"] = "İzleyiciyi Göster",
                ["GuiMenuActToggleFormMainHide"] = "İzleyiciyi Gizle",
                ["GuiMenuActExit"] = "Çıkış",

                // GUI: Menu, runtime-built captions with a value baked into the text
                ["GuiMenuActKbdIdleOff"] = "Boşta kapanma",
                ["GuiMenuActKbdIdleOffDisabled"] = "kapalı",
                ["GuiMenuActGpuDisplayOffHotkey"] = "Ekran kapatma kısayolu",
                ["GuiMenuPowerModePerformance"] = "Başarım",
                ["GuiMenuPowerModeSaver"] = "Güç tasarrufu",
                ["GuiMenuPowerModeBalanced"] = "Dengeli",
                ["GuiMenuPowerModeUnknown"] = "?",
                ["GuiMenuCpuBoostOff"] = "Kapalı",
                ["GuiMenuCpuBoostOn"] = "Açık",
                ["GuiMenuCpuBoostAggressive"] = "Agresif",
                ["GuiMenuCpuBoostOnEfficient"] = "Açık (verimli)",
                ["GuiMenuCpuBoostAggressiveEfficient"] = "Agresif (verimli)",
                ["GuiMenuCpuBoostUnknown"] = "?",

                // GUI: Stat cards
                ["GuiMainCardBatCharging"] = "şarj oluyor",
                ["GuiMainCardBatPluggedIn"] = "prize takılı",
                ["GuiMainCardBatOnBattery"] = "pilde",
                ["GuiMainCardBatNone"] = "pil yok",
                ["GuiMainCardBatCycles"] = "çevrim",
                ["GuiMainCardBatHealth"] = "sağlık",
                ["GuiMainCardGpuIdle"] = "boşta (pilde)",
                ["GuiMainCardGpuOnBattery"] = "pilde",
                ["GuiMainCardNotAvailable"] = "yok",

                // GUI: Battery tooltip
                ["GuiMainBatTipNone"] = "Pil algılanmadı",
                ["GuiMainBatTipBattery"] = "Pil",
                ["GuiMainBatTipPower"] = "Güç",
                ["GuiMainBatTipCharging"] = "şarj oluyor",
                ["GuiMainBatTipDischarging"] = "boşalıyor",
                ["GuiMainBatTipRemaining"] = "Kalan süre",
                ["GuiMainBatTipHealth"] = "Sağlık (yıpranma)",
                ["GuiMainBatTipCycles"] = "Şarj çevrimi",

                // GUI: Details panel row labels (a fixed-width label column)
                ["GuiMainDetSystem"] = "Sistem",
                ["GuiMainDetStatus"] = "Durum",
                ["GuiMainDetUsage"] = "Kullanım",
                ["GuiMainDetGpu"] = "GPU",
                ["GuiMainDetDisk"] = "Disk",
                ["GuiMainDetNetwork"] = "Ağ",
                ["GuiMainDetPower"] = "Güç",
                ["GuiMainDetCore"] = "Çekirdek",
                ["GuiMainDetTemp"] = "Sıcaklık",
                ["GuiMainDetClock"] = "Frekans",
                ["GuiMainDetCaption"] = "Sistem · GPU · Disk · Ağ · Güç · Çekirdekler",

                // GUI: Details panel values
                ["GuiMainDetPluggedIn"] = "Prize takılı",
                ["GuiMainDetThrottle"] = "Kısıtlama",
                ["GuiMainDetThrottleNone"] = "Yok",
                ["GuiMainDetThrottleThermal"] = "Termal",
                ["GuiMainDetThrottlePower"] = "Güç",
                ["GuiMainDetThrottleBoth"] = "Termal+Güç",
                ["GuiMainDetUptime"] = "Açık",
                ["GuiMainDetPlan"] = "Plan",
                ["GuiMainDetLoad"] = "Yük",
                ["GuiMainDetVram"] = "VRAM",
                ["GuiMainDetSsd"] = "SSD",
                ["GuiMainDetRead"] = "Okuma",
                ["GuiMainDetWrite"] = "Yazma",
                ["GuiMainDetDown"] = "İndirme",
                ["GuiMainDetUp"] = "Yükleme",
                ["GuiMainDetLink"] = "bağlantı",
                ["GuiMainDetSystemDraw"] = "Sistem",
                ["GuiMainDetBatterySource"] = "pil",
                ["GuiMainDetCharge"] = "Şarj",
                ["GuiMainDetLeft"] = "Kalan",
                ["GuiMainDetHour"] = "sa",
                ["GuiMainDetMinute"] = "dk",
                ["GuiMainDetNotAvailable"] = "yok",

                // GUI: Thermal protection and throttle notifications
                ["GuiThermalProtectOn"] = "Isıl koruma: fanlar azami hıza ayarlandı",
                ["GuiThermalProtectPanic"] = "Acil ısıl koruma: fan denetimi ürün yazılımına geri verildi",
                ["GuiThermalProtectOff"] = "Isıl koruma kaldırıldı",
                ["GuiThrottleNotify"] = "CPU ısıl kısıtlaması algılandı",

                // GUI: Fan curve editor
                ["GuiCurveTitle"] = "Fan Eğrisi Düzenleyicisi",
                ["GuiCurveHint"] = "Noktaları sürükleyin: X = sıcaklık (°C), Y = fan hızı (%). Uygula, eğriyi başarım modunda çalıştırır.",
                ["GuiCurveApply"] = "Uygula",
                ["GuiCurveDefault"] = "Varsayılan",
                ["GuiCurveStop"] = "Durdur",
                ["GuiCurveClose"] = "Kapat",
                ["GuiCurveApplied"] = "Uygulandı (başarım modu)",
                ["GuiCurveStopped"] = "Durduruldu",
                ["GuiCurveError"] = "Hata:",

                // GUI: Tooltips
                ["GuiTipBtnAccept"] = "Onayla ve devam et",
                ["GuiTipBtnCancel"] = "İptal et ve pencereyi kapat",
                ["GuiTipFan0Cap"] = "Sol taraf birinci (CPU) fanın okumalarını gösterir",
                ["GuiTipFan1Cap"] = "Sağ taraf ikinci (GPU) fanın okumalarını gösterir",
                ["GuiTipFanUnitVal"] = "Fan hızı dakikadaki devir sayısıyla (rpm) ölçülür",
                ["GuiTipFan0Val"] = "Gerçek zamanlı CPU fan hızı okuması [rpm]",
                ["GuiTipFan1Val"] = "Gerçek zamanlı GPU fan hızı okuması [rpm]",
                ["GuiTipFanUnitRte"] = "Fanın bağıl oranı yüzde (%) olarak ölçülür",
                ["GuiTipFan0Rte"] = "CPU fanının bağıl oranı [%]",
                ["GuiTipFan0RteBar"] = "CPU fanının bağıl oranı çubuk ölçekte gösterilir",
                ["GuiTipFan1Rte"] = "GPU fanının bağıl oranı [%]",
                ["GuiTipFan1RteBar"] = " GPU fanının bağıl oranı çubuk ölçekte gösterilir" + Environment.NewLine + " Başlangıç noktasının sağ tarafta olduğuna dikkat edin",
                ["GuiTipFan0Lvl"] = "CPU fan seviyesi [krpm]" + Environment.NewLine + "Özel hız: kaydırıcıyı hareket ettirin" + Environment.NewLine + "ve uygulamak için düğmeye tıklayın",
                ["GuiTipFan1Lvl"] = "GPU fan seviyesi [krpm]" + Environment.NewLine + "Özel hız: kaydırıcıyı hareket ettirin" + Environment.NewLine + "ve uygulamak için düğmeye tıklayın",
                ["GuiTipFanCountdown"] = "Geçerliyse bu alan, BIOS otomatik varsayılanlara" + Environment.NewLine + "dönene kadar kalan süreyi gösterir" + Environment.NewLine + "Sayacın dolmasını önlemek için Sabit seçeneğini kullanın",
                ["GuiTipFanProg"] = "Fan programı" + Environment.NewLine + "Hız, tercihlerinize göre" + Environment.NewLine + "sıcaklığı izleyecektir",
                ["GuiTipFanProgCmb"] = "Açılır listeden bir fan programı seçin",
                ["GuiTipFanAuto"] = "Otomatik kip (varsayılan ayar)",
                ["GuiTipFanMode"] = "Açılır listeden bir fan modu seçin",
                ["GuiTipFanConst"] = "Sabit hız kipi" + Environment.NewLine + "Her fanın seviyesini kaydırıcılarla ayarlayın",
                ["GuiTipFanMax"] = "Azami hız kipi" + Environment.NewLine + "Fanlar azami hızda çalışır" + Environment.NewLine + "(5.500 ve 5.700 rpm)",
                ["GuiTipFanOff"] = "Fanlar kapalı" + Environment.NewLine + "Fanları tümüyle durdurur",
                ["GuiTipFanSet"] = "Geçerli ayarları uygulamak için tıklayın" + Environment.NewLine + "Ayarlar değiştiğinde düğme vurgulanır",
                ["GuiTipKbdBacklight"] = "Klavye arka aydınlatmasını aç veya kapat",
                ["GuiTipKbdColorPreset"] = "Uygulanacak bir renk hazır ayarını" + Environment.NewLine + "açılır kutudan seçin",
                ["GuiTipKbdColorPresetDel"] = "Seçili hazır ayarı sil",
                ["GuiTipKbdColorPresetSet"] = "Geçerli ayarları hazır ayar olarak kaydet",
                ["GuiTipKbdColorVal"] = "Bu parametreyle renkleri onaltılık değerleriyle ayarlayın" + Environment.NewLine + "Komut satırından renk ayarlamak için: StarMon -Bios Color=<Param>",
                ["GuiTipKbdPic"] = "Rengi değiştirmek için klavyenin herhangi bir yerine tıklayın," + Environment.NewLine + "değişiklikler anında uygulanır",
                ["GuiTipSys"] = "Sistem durumu bilgisi burada gösterilir",
                ["GuiTipTmpCPUT"] = "CPU Sıcaklığı",
                ["GuiTipTmpGPTM"] = "GPU Sıcaklığı",
                ["GuiTipTmpBIOS"] = "BIOS tarafından bildirilen sıcaklık" + Environment.NewLine + "Gözlemlenen değerler diğer sensörlere" + Environment.NewLine + "kıyasla çok daha düşüktür",
                ["GuiTipTmpIRSN"] = "Kızılötesi Sensör Sıcaklığı",
                ["GuiTipTmpRTMP"] = "Platform Denetleyici Hub Sıcaklığı",
                ["GuiTipTmpTMP1"] = "Bellek Sıcaklığı",
                ["GuiTipTmpTNT2"] = "Yorumu Bilinmiyor",
                ["GuiTipTmpTNT3"] = "Depolama",
                ["GuiTipTmpTNT4"] = "Depolama",
                ["GuiTipTmpTNT5"] = "Yorumu Bilinmiyor",
                ["GuiTipTmpUnknown"] = "Özel Sensör",
                ["GuiTipTxtInput"] = "Değeri girin",

                // GUI: Main, stat-card and details-panel tooltips
                ["GuiTipCardCpu"] =
                    "Geçerli işlemci (CPU) sıcaklığı." + Environment.NewLine +
                    "Alt satır: yük yüzdesi · güç tüketimi (W) · çekirdek hızı (GHz)." + Environment.NewLine +
                    "Sıcaklık arttıkça renk maviden kırmızıya doğru kayar.",
                ["GuiTipCardGpu"] =
                    "Geçerli ekran kartı (GPU) sıcaklığı." + Environment.NewLine +
                    "Alt satır: yük yüzdesi · güç tüketimi (W) · çekirdek hızı (GHz)." + Environment.NewLine +
                    "NVIDIA yoklaması pilde kapalıyken değerler boş kalabilir.",
                ["GuiTipCardFan"] =
                    "Fan hızı." + Environment.NewLine +
                    "Büyük değer en yüksek fan hızıdır (rpm); alt satır CPU ve GPU fan yüzdelerini gösterir.",
                ["GuiTipSysInfo"] =
                    "Sistem özeti:" + Environment.NewLine +
                    "Sistem satırı — üretici/model, üretim tarihi, CPU güç sınırı (PL4) ve adaptör/pil durumu." + Environment.NewLine +
                    "Durum satırı — GPU modu, D-state ve kısıtlama durumu." + Environment.NewLine +
                    "Kullanım satırı — RAM kullanımı, çalışma süresi ve etkin güç planı.",
                ["GuiTipExtra"] =
                    "GPU, G/Ç ve güç ayrıntıları:" + Environment.NewLine +
                    "GPU satırı — yük, sıcaklık, güç (W), hızlar (çekirdek/bellek) ve NVAPI üzerinden VRAM." + Environment.NewLine +
                    "Disk satırı — okuma/yazma aktarım hızıyla birlikte SSD sıcaklığı." + Environment.NewLine +
                    "Ağ satırı — indirme/yükleme hızları ve Wi-Fi bağlantısı (sinyal, bağlantı hızı)." + Environment.NewLine +
                    "Güç satırı — CPU/GPU tüketimi, pil akışı (sistem tüketimi) ve öngörülen pil süresi.",
                // Bu anahtarın tarif ettiği çekirdek tablosu Windows Forms
                // arayüzüyle birlikte gitti; aşağıda ikinci kez tanımlandığı
                // için buradaki tanım zaten hiç kullanılmıyordu.
                ["GuiTipGrpDetails"] =
                    "Ayrıntılar paneli: sistem, GPU, disk/ağ ve işlemci çekirdeği bilgileri." + Environment.NewLine +
                    "Açıklaması için her satırın üzerine gelin.",

                // GUI: Hotkey capture dialog (Gui.cs)
                ["GuiHotkeyNotAssigned"] = "atanmadı",
                ["GuiHotkeyModCtrl"] = "Ctrl+",
                ["GuiHotkeyModAlt"] = "Alt+",
                ["GuiHotkeyModShift"] = "Shift+",
                ["GuiHotkeyModWin"] = "Win+",
                ["GuiHotkeyDialogTitle"] = "Ekran kapatma kısayolu",
                ["GuiHotkeyInstructions"] =
                    "Atamak istediğiniz tuş bileşimine basın." + Environment.NewLine +
                    "En az bir değiştirici tuş gereklidir (Ctrl / Alt / Shift).",
                ["GuiHotkeyOk"] = "Tamam",
                ["GuiHotkeyClear"] = "Temizle",
                ["GuiHotkeyCancel"] = "İptal",

                // GUI: Hardware capabilities dialog (GuiFormCaps.cs)
                ["GuiCapsTitle"] = "Donanım Yetenekleri",
                ["GuiCapsGathering"] = "Donanım yetenekleri toplanıyor…",
                ["GuiCapsCopy"] = "Kopyala",
                ["GuiCapsClose"] = "Kapat",
                ["GuiCapsBuildError"] = "Rapor oluşturulamadı: ",

                // GUI: Log viewer (GuiFormLog.cs)
                ["GuiLogTitle"] = "— Günlük Görüntüleyici",
                ["GuiLogClear"] = "Temizle",
                ["GuiLogExport"] = "Dışa Aktar",
                ["GuiLogPause"] = "Duraklat",
                ["GuiLogResume"] = "Devam Ettir",
                ["GuiLogAutoScroll"] = "Otomatik Kaydırma",
                ["GuiLogSearch"] = "Ara:",
                ["GuiLogFilter"] = "Süzgeç:",
                ["GuiLogFilterBios"] = "BIOS",
                ["GuiLogFilterEc"] = "EC",
                ["GuiLogFilterHardware"] = "Donanım",
                ["GuiLogFilterError"] = "Hata",
                ["GuiLogFilterInfo"] = "Bilgi",
                ["GuiLogFilterGui"] = "GUI",
                ["GuiLogEntries"] = "{0} girdi",
                ["GuiLogSaveFilter"] = "Metin dosyası (*.txt)|*.txt|Günlük dosyası (*.log)|*.log",
                ["GuiLogSaveSuccess"] = "Günlük dosyası başarıyla kaydedildi.",
                ["GuiLogSaveFail"] = "Günlük dosyası kaydedilemedi.",
                ["GuiLogErrorCaption"] = "Hata",

                // GUI: History graph context menu and hover hint (SparklineGraph.cs)
                ["GuiGraphCopy"] = "Panoya kopyala",
                ["GuiGraphSavePng"] = "PNG olarak kaydet…",
                ["GuiGraphExportCsv"] = "Verileri CSV olarak dışa aktar…",
                ["GuiGraphTimeWindow"] = "Zaman aralığı",
                ["GuiGraphRangeShort"] = "Kısa",
                ["GuiGraphRangeMedium"] = "Orta",
                ["GuiGraphRangeLong"] = "Uzun",
                ["GuiGraphHintTitle"] = "Geçmiş grafiği — zaman içindeki değişimler",
                ["GuiGraphHintLegend"] = "Gösterge üzerine tıklayın: bir seriyi gizle/göster · ",
                ["GuiGraphHintContext"] = "Sağ tık: kopyala, PNG kaydet, zaman aralığı",
                ["GuiGraphFilterPng"] = "PNG görüntüsü|*.png",
                ["GuiGraphFilterCsv"] = "Virgülle ayrılmış değerler|*.csv",

                // Data formats
                ["DataTypeBool"] = "<Bayrak>",
                ["DataSyntaxBool"] = "<On|True|Yes|1> | <Off|False|No|0>",

                ["DataTypeByte"] = "<Bayt>",
                ["DataSyntaxByte"] = "<0-255|0x00-0xFF|0b00000000-0b11111111>",

                ["DataTypeByteArray"] = "<BaytDizisi>",
                ["DataSyntaxByteArray"] = "<00-FF>+",

                ["DataTypeColor4"] = "<Renk>",
                ["DataSyntaxColor4"] = "<HazırAyarAdı> | <RGB0>:<RGB1>:<RGB2>:<RGB3> (<RGB#>: 000000-FFFFFF)",

                ["DataTypeFanLevel"] = "<FanSeviyesi>",
                ["DataSyntaxFanLevel"] = "<Fan1>,<Fan2> (<Fan#>: 0-255|0x00-0xFF|0b00000000-0b11111111)",

                ["DataTypeFanMode"] = "<FanModu>",
                ["DataSyntaxFanMode"] = "<FanModuId|0-255|0x00-0xFF|0b...> (<FanModuId>: Default|Performance|Cool|L#, <#>: 0-8)",

                ["DataTypeFanTable"] = "<FanTablosu>",
                ["DataSyntaxFanTable"] = "<Fan1>,<Fan2>,<Sıcaklık>[:...[:...]] (<Fan#>, <Sıcaklık>: <Bayt>)",

                ["DataTypeGpuMode"] = "<GpuModu>",
                ["DataSyntaxGpuMode"] = "<GpuModuId|0-255|0x00-0xFF|0b...> (<GpuModuId>: Hybrid|Discrete|Optimus)",

                ["DataTypeGpuPowerLevel"] = "<GpuHazırAyar>",
                ["DataSyntaxGpuPowerLevel"] = "Max[imum] | Med[ium]|Mid[dle] | Min[imum]",

                ["DataTypeReg"] = "<Yazmaç>",
                ["DataSyntaxReg"] = "<AD|0-255|0x00-0xFF|0b00000000-0b11111111>",
                ["DataSyntaxOrTwo"] = "[(2)]",

                ["DataTypeTName"] = "<GörevAdı>",
                ["DataSyntaxTName"] = "Gui (Açılışta Otomatik Başlatma) | Key (Omen Tuşu Yakalama) | Mux (Advanced Optimus Düzeltmesi)",

                ["DataTypeWord"] = "<Sözcük>",
                ["DataSyntaxWord"] = "<0-65535|0x0000-0xFFFF|0b0000000000000000-0b1111111111111111>",

                // Error messages
                ["ErrArgUnknown"] = "Bilinmeyen argüman",
                ["ErrBiosCall"] = "BIOS çağrısı başarısız oldu",
                ["ErrBiosInit"] = "BIOS denetimleri başlatılamadı. Uyumlu bir HP sisteminiz olduğundan ve ACPI\\PNP0C14 sürücüsünün kurulu olduğundan emin olun.",
                ["ErrBiosNull"] = "BIOS denetimleri örneklenemedi",
                ["ErrBiosSend"] = "BIOS çağrısı yapılamadı",
                ["ErrBiosSendCommand"] = "Komut kullanılamıyor",
                ["ErrBiosSendSize"] = "Giriş veya çıkış boyutu çok küçük",
                ["ErrBiosSendUnknown"] = "BIOS'tan bilinmeyen yanıt: {0}",
                ["ErrConfigLoad"] = "Yapılandırma verisi yüklenemedi",
                ["ErrConfigSave"] = "Yapılandırma verisi kaydedilemedi",
                ["ErrEcInit"] = "Gömülü denetleyici başlatılamadı",
                ["ErrEcLock"] = "Gömülü denetleyici için dışlayıcı kilit alınamadı",
                ["ErrProbeWrite"] = "Donanım raporu yazılamadı",
                ["ErrEcNull"] = "Gömülü denetleyici örneklenemedi",
                ["ErrFileSave"] = "Dosya kaydedilemedi",
                ["ErrLocaleNull"] = "Yerelleştirilebilir mesaj sistemi örneklenemedi",
                ["ErrLocaleLoad"] = "Yerelleştirilebilir mesajlar dış dosyadan yüklenemedi",
                ["ErrNeedRegisterRead"] = "Okunacak bir yazmaç bekleniyordu",
                ["ErrNeedRegisterWrite"] = "Yazılacak bir yazmaç bekleniyordu",
                ["ErrNeedValueBool"] = "Bir Boole bayrağı bekleniyordu",
                ["ErrNeedValueByte"] = "Ayarlanacak bir bayt değeri bekleniyordu",
                ["ErrNeedValueByteArray"] = "Ayarlanacak bir bayt dizisi değeri bekleniyordu",
                ["ErrNeedValueColor4"] = "Dört renk değerinden oluşan bir dizi bekleniyordu",
                ["ErrNeedValueFanLevel"] = "Bir çift fan hız seviyesi bekleniyordu",
                ["ErrNeedValueFanMode"] = "Bir fan modu bekleniyordu",
                ["ErrNeedValueFanTable"] = "Fan tablosu girdilerinden oluşan bir dizi bekleniyordu",
                ["ErrNeedValueGpuMode"] = "Bir GPU modu bekleniyordu",
                ["ErrNeedValueGpuPowerLevel"] = "Bir GPU güç hazır ayarı bekleniyordu",
                ["ErrNeedValueWord"] = "Ayarlanacak bir sözcük değeri bekleniyordu",
                ["ErrNotImplemented"] = "Uygulanmadı",
                ["ErrProgName"] = "Böyle bir program yok",
                ["ErrProgNone"] = "Yapılandırılmış program yok",
                ["ErrUnexpected"] = "İstisna",
                ["ErrUnexpectedReally"] = "Ayrıntı yok",

                // Program
                ["Prog"] = "Program",
                ["ProgAlt"] = "[Alt]",
                ["ProgEnd"] = "Program Sona Erdi",
                ["ProgModeDefault"] = "Varsayılan",
                ["ProgModePerformance"] = "Performans",
                ["ProgModeCool"] = "Serin",
                ["ProgModeQuiet"] = "Sessiz",
                ["ProgModeExtreme"] = "Aşırı",
                ["ProgFans"] = "Fanlar",
                ["ProgLvl"] = "Svy",
                ["ProgT"] = "S",
                ["ProgSubMax"] = "azm",

                // GUI: the WPF window
                // Sekme adları başlık çubuğuna sığmak zorunda: "Gösterge
                // paneli" tek başına altı sekmenin dörtte birini yiyordu
                ["GuiWpfDashboard"] = "Panel",
                ["GuiWpfSensors"] = "Sensörler",
                ["GuiWpfSensorsCaption"] = "TÜM ÖLÇÜMLER",
                ["GuiWpfSensorsHint"] = "Makinenin bildirdiği her şey, gruplanmış ve canlı güncellenir. Gösterge paneli öne çıkan değerleri gösterir; burası tam listedir.",
                ["GuiWpfCurve"] = "Fan eğrisi",
                ["GuiWpfCooling"] = "Soğutma",
                ["GuiWpfKeyboard"] = "Klavye",
                ["GuiWpfLog"] = "Günlük",
                ["GuiWpfSystem"] = "Sistem",
                ["GuiWpfAbout"] = "Hakkında",

                ["GuiWpfHottest"] = "EN SICAK",
                ["GuiWpfCpu"] = "İŞLEMCİ",
                ["GuiWpfGpu"] = "EKRAN KARTI",
                ["GuiWpfFans"] = "FANLAR",
                ["GuiWpfBattery"] = "PİL",

                // Sekmelerin altındaki özet şeridi. Kart başlıklarından ayrı
                // anahtarlar, çünkü çok daha kısa olmak zorundalar: dört
                // etiket, dört değer, iki eğri ve üç rozet tek satırı
                // paylaşıyor.
                ["GuiWpfStripCpu"] = "CPU",
                ["GuiWpfStripGpu"] = "GPU",
                ["GuiWpfStripFan"] = "FAN",
                ["GuiWpfStripBattery"] = "PİL",

                // Rozetler. Makinenin, kullanıcının seçmediği bir durumda
                // olduğunu söylerler — pencerenin bugüne dek hiç söyleyemediği
                // şey.
                // Gösterge panelindeki blokların tablo satırları. Kısa: çeyrek
                // genişlikte bir kartta değerlerinin yanında duruyorlar.
                ["GuiWpfRowLoad"] = "Yük",
                ["GuiWpfRowPower"] = "Güç",
                ["GuiWpfRowLimits"] = "Sınır",
                ["GuiWpfRowClock"] = "Frekans",
                ["GuiWpfRowVram"] = "VRAM",
                ["GuiWpfRowFanCpu"] = "CPU fanı",
                ["GuiWpfRowFanGpu"] = "GPU fanı",
                ["GuiWpfRowHottest"] = "En sıcak",
                ["GuiWpfRowCeiling"] = "Tavan",
                ["GuiWpfRowCharge"] = "Şarj",
                ["GuiWpfRowFlow"] = "Akış",
                ["GuiWpfRowPlan"] = "Plan",
                ["GuiWpfRowUptime"] = "Açık",
                ["GuiWpfCoreClocks"] = "Çekirdek frekansları",
                ["GuiWpfDiskRate"] = "Disk hızı",
                ["GuiWpfNetRate"] = "Ağ hızı",
                ["GuiTipDiskRate"] = "Önyükleme sürücüsünün saniyede gerçekten kaç megabayt okuyup yazdığı. Sıcaklığından ayrıdır: o ne kadar çalıştığını söyler, bu şu anda ne kadar çalıştığını.",
                ["GuiTipNetRate"] = "Ağ üzerinde gerçekten hareket eden trafik, saniyede megabit. Üstteki bağlantı hızı taşıyabileceğidir; bu, taşıdığıdır.",
                ["GuiTipPowerMode"] = "Windows'un kendi güç modu — pil menüsündeki sürgü. Üstündeki güç planıyla aynı şey değildir.",
                ["GuiTipUptime"] = "Makinenin son açılışından bu yana ne kadar süredir çalıştığı.",
                ["GuiTipCountdown"] = "Gömülü Denetleyici'nin emniyet sayacı. Sıfıra ulaştığında, elle ne ayarlanmış olursa olsun ürün yazılımı fanları geri alır.",
                ["GuiTipHpFan"] = "Ürün yazılımının kendi sensör arayüzü üzerinden, fana verdiği kendi adıyla yayımladığı fan hızı. Gömülü Denetleyici'nin takometresinin güvenilmez olduğu yerde dürüst olan budur.",
                ["GuiWpfRowMemClock"] = "Bellek frekansı",
                ["GuiWpfRowTgp"] = "TGP",
                ["GuiWpfSystemBlock"] = "SİSTEM",
                ["GuiWpfRowMemory"] = "Bellek",
                ["GuiWpfRowDisk"] = "Disk",
                ["GuiWpfRowNetwork"] = "Ağ",
                ["GuiWpfRowMode"] = "Mod",
                ["GuiWpfRowCountdown"] = "Geri sayım",
                ["GuiWpfRowGuard"] = "Koruma",
                ["GuiWpfRowHealth"] = "Sağlık",
                ["GuiWpfRowCycles"] = "Döngü",

                // Fan seviye tavanının nereden geldiği. Profil bunu her
                // açılışta hesaplıyor ve tek ulaştığı yer bir günlük satırıydı;
                // bu da sürgünün sınırını birinin uydurduğu bir sayı gibi
                // gösteriyordu.
                ["GuiWpfCeilingTable"] = "fan tablosundan",
                ["GuiWpfCeilingMaximum"] = "azamide görüldü",
                ["GuiWpfCeilingRunning"] = "çalışırken görüldü",
                ["GuiWpfCeilingSet"] = "ayarlanmış",
                ["GuiWpfCeilingFixed"] = "elle sabitlenmiş",
                ["GuiWpfCountdownOff"] = "kapalı",
                ["GuiWpfGuardActive"] = "devrede",
                ["GuiWpfGuardIdle"] = "izliyor",
                ["GuiWpfPowerModeHigh"] = "En iyi performans",
                ["GuiWpfPowerModeBalanced"] = "Dengeli",
                ["GuiWpfPowerModeSaver"] = "En iyi verim",
                ["GuiWpfCoreClockTip"] = "Her mantıksal çekirdeğin çalıştığı frekans. Sıcaklık bantları yerine tek renk: yavaş çalışan bir çekirdek sorunlu bir çekirdek değildir.",

                // Grafiğin penceresi ve dışa aktarımı
                ["GuiWpfWindowShort"] = "2 dk",
                ["GuiWpfWindowMedium"] = "5 dk",
                ["GuiWpfWindowLong"] = "10 dk",
                ["GuiWpfTipWindow"] = "Çiziminin ne kadar geriye uzandığı. Değiştirmek, o ana dek kaydedilmiş geçmişi korur.",
                ["GuiWpfTipExportCsv"] = "Çizilen geçmişi, her örnek bir satır olacak şekilde CSV dosyası olarak kaydet.",

                // Soğutma bölümü: eğri, kayıtlı programlar ve ürün
                // yazılımının gerçekten dayattığı sınırlar
                // Sistem bölümü
                ["GuiWpfLicenceCaption"] = "LİSANS",
                ["GuiWpfMachineCaption"] = "BU MAKİNE",
                ["GuiWpfProfileCaption"] = "TESPİT EDİLENLER",
                ["GuiWpfBiosCaption"] = "BIOS KURULUMU",
                ["GuiWpfBiosHint"] = "Ürün yazılımının yayımladığı her ayar, açılışta bir kez okunur. Salt okunur: bunlar buradan değil, BIOS kurulum ekranından değiştirilir.",
                ["GuiWpfBiosSearchTip"] = "Hem ada hem değere göre süzer; böylece Enabled aramak açık olan her seçeneği bulur.",
                ["GuiWpfReportCaption"] = "TAM DONANIM RAPORU",
                ["GuiWpfReportHint"] = "Uygulamanın bu makine hakkında saptayabildiği her şey, tek metinde. Bir sorun bildirirken eklenecek şey budur.",
                ["GuiWpfProbing"] = "ürün yazılımına soruluyor...",
                ["GuiWpfPowerModeCaption"] = "WINDOWS GÜÇ MODU",
                ["GuiWpfPowerModeHint"] = "Pil menüsündeki sürgünün aynısı. Güç planı değildir, gösterge panelindeki ürün yazılımı performans profili de değildir — üçü de vardır ve üçü de önemlidir.",
                ["GuiWpfRowFamily"] = "Aile",
                ["GuiWpfRowExtreme"] = "Extreme modu",
                ["GuiWpfRowZones"] = "Klavye bölgeleri",
                ["GuiWpfRowRefresh"] = "Yenileme hızları",
                ["GuiWpfRowProbed"] = "Yoklama",
                ["GuiWpfNoColour"] = "renk yok",
                // Yalnızca yapılandırma dosyasından ulaşılabilen ayarlar
                ["GuiWpfFanSection"] = "FANLAR",
                ["GuiWpfFanSectionHint"] = "Bunlar fan sürgülerini ve eğriyi ölçekler. Uygulama tavanı açılışta kendi belirler; elle ayarlamak yalnızca kendi tavanını yanlış bildiren bir anakart içindir.",
                ["GuiWpfFanCeiling"] = "Seviye tavanı",
                ["GuiWpfFanFloor"] = "Seviye tabanı",
                ["GuiWpfCurveHysteresis"] = "Eğri histerezisi",
                ["GuiWpfTipHysteresis"] = "Seviyenin geri inmesi için sıcaklığın eğri basamağının ne kadar altına düşmesi gerektiği. Sıfır, eğriyi birebir izler ve bir ölçüm sınırda durduğunda fanların dalgalanmasına yol açar.",
                ["GuiWpfFanAutoDetect"] = "Fanlar daha yüksekte görülünce tavan yükselebilsin",
                ["GuiWpfKeepFansSet"] = "Elle ayarlanan fan hızı kendiliğinden geri dönmesin",
                ["GuiWpfSuspendOnSleep"] = "Makine uykudayken fan programını askıya al",
                ["GuiWpfDisplaySection"] = "EKRAN",
                ["GuiWpfRefreshOnAc"] = "PRİZDE",
                ["GuiWpfRefreshOnBattery"] = "PİLDE",
                ["GuiWpfRefreshAutoDetect"] = "Hızları ekranın bildirdiklerinden al",
                ["GuiWpfHotkey"] = "EKRANI KAPAT",
                ["GuiWpfHotkeyHint"] = "Ekranı karartan genel bir kısayol. Özellik yazıldığından beri açılışta kaydediliyor ve seçmenin hiçbir yolu yoktu.",
                ["GuiWpfHotkeyNone"] = "Atanmadı",
                ["GuiWpfHotkeyPress"] = "Bir kombinasyona bas...",
                ["GuiWpfHotkeyClear"] = "Kaldır",
                ["GuiWpfOmenKey"] = "OMEN TUŞU",
                ["GuiWpfOmenKeyHint"] = "Klavyedeki özel tuşun ne yaptığı. Komut çalıştırmak, fan programının önüne geçer.",
                ["GuiWpfKeyToggles"] = "Fan programını başlat ve durdur",
                ["GuiWpfKeyCycles"] = "Kayıtlı bütün programlar arasında dolaş",
                ["GuiWpfKeyShowsFirst"] = "İlk basışta pencereyi göster",
                ["GuiWpfKeySilent"] = "Program değişince bildirim gösterme",
                ["GuiWpfKeyRuns"] = "Bunun yerine bir komut çalıştır",
                ["GuiWpfKeyCommandTip"] = "Çalıştırılacak program. Tam yol ya da arama yolundaki herhangi bir şey.",
                ["GuiWpfKeyArgumentsTip"] = "Komuta geçirilecek argümanlar.",
                ["GuiWpfKeyMinimised"] = "Simge durumunda başlat",
                ["GuiWpfReportBiosErrors"] = "Bu makinenin reddettiği ürün yazılımı çağrılarını günlüğe yaz",
                ["GuiWpfLogFileSize"] = "Dosyayı şu boyutta yenile",
                ["GuiWpfCadence"] = "NE SIKLIKTA",
                ["GuiWpfCadenceHint"] = "Donanıma ne sıklıkta soru sorulduğu. Düşük olan daha çabuk yanıt verir ve daha pahalıdır; çalışırken değiştirme düzeneği hep vardı, değiştirecek bir denetim yoktu.",
                ["GuiWpfCadenceMonitor"] = "Pencere açıkken",
                ["GuiWpfCadenceRecord"] = "Pencere gizliyken",
                ["GuiWpfCadenceProgram"] = "Fan programı adımı",
                ["GuiWpfCoolingState"] = "BU MAKİNENİN İZİN VERDİKLERİ",
                ["GuiWpfPrograms"] = "FAN PROGRAMLARI",
                ["GuiWpfRun"] = "Çalıştır",
                ["GuiWpfDelete"] = "Sil",
                ["GuiWpfSave"] = "Kaydet",
                ["GuiWpfSteps"] = "adım",
                ["GuiWpfRowSoftware"] = "Yazılım denetimi",
                ["GuiWpfRowAlwaysOn"] = "Fanlar hep açık",
                ["GuiWpfRowFanCount"] = "Fan sayısı",
                ["GuiWpfRowLevelPath"] = "Seviye yazma yolu",
                ["GuiWpfYes"] = "var",
                ["GuiWpfNo"] = "yok",
                ["GuiWpfUnknown"] = "belirtilmemiş",
                ["GuiWpfTipCeiling"] = "Bu anakartın kabul ettiği en yüksek fan seviyesi ve uygulamanın bunu nasıl belirlediği. Sürgüler ve eğri buna göre ölçeklenir.",
                ["GuiWpfTipSoftware"] = "Ürün yazılımının yazılımla fan denetimi sunduğunu kabul edip etmediği. Sunmadığı yerde, yazılan bir seviye sessizce yok sayılabilir.",
                ["GuiWpfTipAlwaysOn"] = "Bir BIOS kurulum seçeneği. Açıkken fanlar hangi seviye istenirse istensin hiç durmaz — sessizleşmeyen bir makinenin açıklaması budur.",
                ["GuiWpfTipLevelPath"] = "Fan seviyelerinin BIOS arayüzüyle mi yoksa doğrudan Gömülü Denetleyici'ye mi yazıldığı. Anakartlar farklıdır ve yanlış yol sessizce yok sayılır.",
                ["GuiWpfTipRun"] = "Seçili programı başlat. Pencere kapalıyken de çalışmaya devam eder.",
                ["GuiWpfTipDelete"] = "Seçili programı yapılandırma dosyasından kaldır.",
                ["GuiWpfTipSave"] = "Yukarıdaki eğriyi bu adla program olarak kaydet. Aynı adlı bir program varsa değiştirilir.",
                ["GuiWpfProgramRunning"] = "{0} çalışıyor",
                ["GuiWpfProgramStopped"] = "Fan programı durduruldu",
                ["GuiWpfProgramSaved"] = "{0} kaydedildi",
                ["GuiWpfProgramDeleted"] = "{0} silindi",
                ["GuiWpfProgramGone"] = "Bu program artık yapılandırmada yok",
                ["GuiWpfChipProtection"] = "ISIL KORUMA",
                ["GuiWpfChipThrottle"] = "KISITLAMA",
                ["GuiWpfChipProgram"] = "PROGRAM",
                ["GuiWpfTipProtection"] = "Uygulama, makineyi korumak için fanları azamide tutuyor. Bu sizin yaptığınız bir ayar değil — sıcaklık düşünce kendiliğinden kalkar.",

                // Grafik göstergesi (kısa tutulmuştur)
                ["GuiWpfSeriesCpuFan"] = "CPU fanı",
                ["GuiWpfSeriesGpuFan"] = "GPU fanı",
                ["GuiWpfSeriesLoad"] = "Yük",
                ["GuiWpfSeriesPower"] = "Güç",

                ["GuiWpfFanControl"] = "FAN DENETİMİ",
                ["GuiWpfFanAutomatic"] = "Otomatik",
                ["GuiWpfFanConstant"] = "Sabit",
                ["GuiWpfFanMaximum"] = "Azami",
                ["GuiWpfFanProgram"] = "Program",

                ["GuiWpfGraphicsPower"] = "EKRAN KARTI GÜCÜ",
                ["GuiWpfPerfMode"] = "PERFORMANS MODU",

                // Live supporting-line text (battery state, throttle, fan level)
                ["GuiWpfBatNone"] = "pil yok",
                ["GuiWpfBatCharging"] = "şarj oluyor",
                ["GuiWpfBatAc"] = "prizde",
                ["GuiWpfBatDc"] = "pilde",
                ["GuiWpfThrottleThermalPower"] = "Isıl + güç",
                ["GuiWpfThrottleThermal"] = "Isıl",
                ["GuiWpfThrottlePower"] = "Güç",
                ["GuiWpfThrottleNone"] = "Yok",
                ["GuiWpfLevelFmt"] = "seviye {0} / {1} · maks. {2}",
                ["GuiWpfCouldNotApply"] = "Uygulanamadı: {0}",
                ["GuiWpfGpuBase"] = "Temel",
                ["GuiWpfGpuCustom"] = "Özel TGP",
                ["GuiWpfGpuBoost"] = "Boost",
                ["GuiWpfNotAvailable"] = "bu modelde yok",

                ["GuiWpfCurveCaption"] = "FAN EĞRİSİ",
                ["GuiWpfCurveHint"] =
                    "Fanların o sıcaklıkta ne kadar çalışacağını ayarlamak için bir noktayı sürükleyin. "
                    + "Eğriyi uygulamak onu Performans kipinde bir fan programı olarak çalıştırır.",
                ["GuiWpfReset"] = "Sıfırla",
                ["GuiWpfStop"] = "Durdur",
                ["GuiWpfApply"] = "Uygula",
                ["GuiWpfApplied"] = "Uygulandı",

                ["GuiWpfBacklight"] = "ARKA IŞIK",
                ["GuiWpfColour"] = "RENK",
                ["GuiWpfMode"] = "MOD",
                ["GuiWpfKbdStatic"] = "Sabit renk",
                ["GuiWpfKbdTemperature"] = "Sıcaklığı izle",
                ["GuiWpfKbdCycle"] = "Renk döngüsü",
                ["GuiWpfKbdBreathe"] = "Nefes efekti",
                ["GuiWpfKbdSpeed"] = "EFEKT HIZI",
                ["GuiWpfKbdIdleOff"] = "BOŞTAYKEN KAPAT",
                ["GuiWpfKbdNever"] = "asla",
                ["GuiWpfKbdMinutes"] = "dk",
                ["GuiWpfKbdPresets"] = "KAYITLI RENKLER",
                ["GuiWpfZoneLeft"] = "SOL",
                ["GuiWpfZoneCentre"] = "ORTA",
                ["GuiWpfZoneRight"] = "SAĞ",
                ["GuiWpfZoneWasd"] = "WASD",
                ["GuiWpfZoneAll"] = "KLAVYE",

                ["GuiWpfLogCaption"] = "GÜNLÜK",
                ["GuiWpfPause"] = "DURAKLAT",
                ["GuiWpfClear"] = "Temizle",
                ["GuiWpfSearch"] = "Ara",
                ["GuiWpfFilterProblems"] = "Sorunlar",
                ["GuiWpfFilterHardware"] = "Donanım",
                ["GuiWpfFilterInterface"] = "Arayüz",
                ["GuiWpfFilterBios"] = "BIOS çağrıları",
                ["GuiWpfFilterEc"] = "EC erişimi",
                ["GuiWpfEntries"] = "kayıt",
                ["GuiWpfEntriesOf"] = "/",

                ["GuiWpfSupportCaption"] = "BU MAKİNE NELERİ DESTEKLİYOR",
                ["GuiWpfSupportHint"] =
                    "Desteklenmeyen her şey, hiçbir şey yapmayan bir denetim olarak gösterilmek "
                    + "yerine arayüzün geri kalanından gizlenir.",
                ["GuiWpfTagline"] =
                    "HP Omen ve Victus dizüstü bilgisayarlar için fan, algılayıcı ve klavye denetimi.",
                ["GuiWpfLicence"] =
                    "GPL-3.0 altında yayımlanmıştır. Bazı bölümlerin telif hakkı © 2023-2024 Piotr Szczepański.",
                ["GuiWpfVersion"] = "Sürüm",
                ["GuiWpfBuilt"] = "Derleme",
                ["GuiWpfModel"] = "Model",
                ["GuiWpfBoard"] = "Anakart",
                ["GuiWpfBios"] = "BIOS",
                ["GuiWpfWindows"] = "Windows",

                // Details panel — extra groups and rows
                ["GuiWpfGraphics"] = "EKRAN KARTI",
                ["GuiWpfStorageNet"] = "DEPOLAMA VE AĞ",
                ["GuiWpfBehaviour"] = "DAVRANIŞ",
                // GuiWpfLog değil: bu, ayarlar kartının başlığı. Aynı adı
                // ikinci kez tanımlamak gezinme sekmesinin etiketini eziyordu
                ["GuiWpfLogSection"] = "GÜNLÜK",
                ["GuiWpfThermalGuard"] = "ISIL KORUMA",
                ["GuiWpfThermalGuardHint"] = "En sıcak algılayıcı eşiğe ulaştığında fanları azamiye zorlar, birkaç derece üstünde ise soğutmayı tamamen ürün yazılımına bırakır. Özel bir nedeniniz yoksa açık bırakın.",
                ["GuiWpfThermalThreshold"] = "Eşik",
                ["GuiWpfStartWithWindows"] = "Windows ile birlikte başlat",
                ["GuiWpfApplyOnStart"] = "Kayıtlı ayarları açılışta uygula",
                ["GuiWpfCloseExits"] = "Kapat düğmesi tepsiye gizlemek yerine uygulamadan çıksın",
                ["GuiWpfStayOnTop"] = "Pencereyi diğer pencerelerin üstünde tut",
                ["GuiWpfThrottleNotify"] = "İşlemci kısıtlandığında bildir",
                ["GuiWpfRefreshFollows"] = "Pilde yenileme hızını düşür, prizde geri yükselt",
                ["GuiWpfPollGpuOnBattery"] = "Pilde ekran kartını okumayı sürdür (daha çok güç harcar)",
                ["GuiWpfFourZone"] = "Bu klavyenin dört renk bölgesi var",
                ["GuiWpfLogVerbose"] = "Her donanım alışverişini kaydet (ayrıntılı)",
                ["GuiWpfLogToFile"] = "Günlüğü uygulamanın yanındaki bir dosyaya da yaz",
                // GuiWpfFans değil: o, gösterge panelindeki fan kartının
                // başlığı ve burada yeniden tanımlamak kartın adını
                // "FANLAR VE ANAKART" yapıyordu
                ["GuiWpfFansBoard"] = "FANLAR VE ANAKART",
                ["GuiWpfFanCpuRpm"] = "İşlemci fanı",
                ["GuiWpfFanGpuRpm"] = "Ekran kartı fanı",
                ["GuiWpfSensorChipset"] = "Yonga seti",
                ["GuiWpfSensorMemory"] = "Bellek",
                ["GuiWpfSensorBios"] = "BIOS ölçümü",
                ["GuiWpfSensorProbe"] = "Anakart ölçümü",
                ["GuiWpfSensorZone"] = "Isıl bölge",
                ["GuiWpfSensorHealth"] = "Algılayıcı sağlığı",
                ["GuiWpfSensorHealthOk"] = "tümü normal bildiriyor",
                ["GuiWpfSensorHealthBad"] = "arıza bildirdi",
                ["GuiTipFanRpm"] = "Fanın gerçekten döndüğü hız; ürün yazılımının kendi devir sayacından okunur. Bu makine devir bildirmiyorsa boş kalır — o durumda yukarıdaki seviye ve yüzde dürüst değerlerdir.",
                ["GuiTipBoardSensor"] = "İşlemcinin veya ekran kartının değil, doğrudan anakartın üzerindeki bir sıcaklık ölçümü. Fan eğrisi ve ısıl koruma bunların en yükseğine tepki verir.",
                ["GuiTipSensorHealth"] = "Ürün yazılımının kendi algılayıcıları hakkındaki görüşü. Normal dışı bir değer, makinenin bir parçayı işaretlediği anlamına gelir; okumalar makul görünse bile bakmaya değer.",
                ["GuiWpfTemp"] = "Sıcaklık",
                ["GuiWpfGpuClock"] = "Frekans",
                ["GuiWpfGpuVram"] = "VRAM",
                ["GuiWpfDisk"] = "Disk",
                ["GuiWpfWifi"] = "Wi-Fi",
                ["GuiWpfCores"] = "Çekirdekler",
                ["GuiWpfCoresTip"] = "Her mantıksal işlemci çekirdeğinin sıcaklığı",
                ["GuiWpfMemory"] = "BELLEK",
                ["GuiWpfMemUsed"] = "Kullanım",
                ["GuiWpfLinkSpeed"] = "Bağlantı",
                ["GuiWpfBatState"] = "Durum",
                ["GuiWpfBatPower"] = "Güç çekişi",
                ["GuiWpfPowerLimit"] = "Sınır",
                ["GuiWpfGpuPowerLimit"] = "Güç sınırı",
                ["GuiWpfCopy"] = "Kopyala",
                ["GuiWpfCopyAll"] = "Tümünü kopyala",

                // Üzerine gelince ne işe yaradığını söyleyen ipuçları
                ["GuiWpfTipCpu"] = "İşlemci sıcaklığı; yükü, gücü, frekansı ve her çekirdek için bir çubuk",
                ["GuiWpfTipGpu"] = "Ekran kartı sıcaklığı; yükü, gücü ve frekansı ile",
                ["GuiWpfTipFans"] = "Donanım azamisine göre yüzde olarak fan hızı",
                ["GuiWpfTipBattery"] = "Pil şarjı ve kalan süre",
                ["GuiWpfTipFanAutomatic"] = "Fan denetimini ürün yazılımına bırak",
                ["GuiWpfTipFanConstant"] = "İki fanı da aşağıda belirlediğin seviyede tut",
                ["GuiWpfTipFanMaximum"] = "İki fan da tam hızda; ekran kartı gücü de onunla yükselir",
                ["GuiWpfTipFanProgram"] = "Sıcaklığı izleyen kayıtlı fan programını çalıştır",
                ["GuiWpfTipPerfMode"] = "Ürün yazılımının güç ve ısıl profili. Performans, ekran kartı gücünü taban çekişinin üstüne çıkaran moddur.",
                ["GuiWpfTipGpuPower"] = "Ekran kartı çipinin çekmesine izin verilen güç",
                ["GuiWpfTipLevels"] = "Fan seviyesi; durmuştan donanım azamisine. Yalnızca Sabit modda geçerlidir.",

                // Panel blokları. Bu kartlardaki diğer satırlar Sensörler
                // sayfasının ipuçlarını yeniden kullanır; buradakiler panelin
                // kendine özgü birleşik gösterimleri.
                ["GuiWpfTipGpuMemClock"] = "Kartın kendi belleğinin çalıştığı frekans; ürün yazılımı bunu çekirdekten ayrı yönetir.",
                ["GuiWpfTipFanLine"] = "Bu fana tutması söylenen seviye — donanım azamisinin yüzdesi olarak — ve yanında gerçekten döndüğü hız.",
                ["GuiWpfTipHottest"] = "Makinedeki bütün sıcaklık algılayıcılarının en yükseği. Fan eğrisi de ısıl koruma da bu değere tepki verir.",
                ["GuiWpfTipCharge"] = "Pilde kalan şarj miktarı.",
                ["GuiWpfTipPlanLine"] = "Windows güç planı ve yanında pil menüsündeki güç modu. Bunlar iki ayrı ayardır ve ikisi de geçerlidir.",

                // Sistem sayfası: makinenin ne olduğu ve uygulamanın açılışta
                // onun hakkında neyi saptadığı.
                ["GuiWpfTipVersion"] = "Bu uygulamanın sürümü ve derlendiği tarih.",
                ["GuiWpfTipBoard"] = "Anakartın kendi model kodu. Makinenin hangi ürün yazılımı çağrılarına yanıt verdiğini pazarlama adından çok bu belirler.",
                ["GuiWpfTipWindows"] = "Üzerinde çalışılan Windows sürümü ve derlemesi.",
                ["GuiWpfTipFamily"] = "Ürün yazılımının kendini ait saydığı ürün ailesi.",
                ["GuiWpfTipFanCount"] = "Ürün yazılımının bu makinede olduğunu söylediği fan sayısı. İkisi birlikte sürülür; yani ikinci fan ikinci bir denetim demek değildir.",
                ["GuiWpfTipExtreme"] = "Bu anakartın Extreme performans profilini sunup sunmadığı. Çoğu sunmaz ve sunulup reddedilmek yerine gizlenir.",
                ["GuiWpfTipZones"] = "Klavye arka ışığının ayrı ayrı renklendirilebilen bölge sayısı. Bir anakart fiziksel olarak sahip olduğundan fazlasını bildirebilir.",
                ["GuiWpfTipRefreshRates"] = "Ekranın çalışabildiğini bildirdiği yenileme hızları.",
                ["GuiWpfTipProbed"] = "Uygulamanın açılışta bu soruları ürün yazılımına sorabilmiş olup olmadığı. Soramadıysa yukarıdaki yanıtlar saptama değil, varsayılan değerlerdir.",
                ["GuiWpfTipBiosSetting"] = "Ürün yazılımının yayımladığı hâliyle bir ayar. Burada salt okunur: BIOS kurulum ekranından değiştirilir.",

                // Klavye sayfası.
                ["GuiWpfTipBacklightSwitch"] = "Klavye arka ışığını açar ve kapatır. Durum ürün yazılımına sorulmaz; uygulama kendisi tutar, çünkü bu makinede sormak yanlış yanıt veriyordu.",
                ["GuiWpfTipSwatch"] = "Bu bölge için renk seçiciyi açar. Renk seçilir seçilmez uygulanır.",
                ["GuiWpfTipHex"] = "Bölgenin rengi onaltılık değer olarak; komut satırının aldığı biçimin aynısı.",
                ["GuiWpfTipKbdStatic"] = "Tek renk, sabit tutulur. Aşağıdaki bölgeler bunun için kullanılır.",
                ["GuiWpfTipKbdTemperature"] = "Arka ışık en sıcak algılayıcıyı izler: makine ısındıkça serinden sıcağa doğru değişir.",
                ["GuiWpfTipKbdCycle"] = "Arka ışık renk çemberinde sürekli dolaşır.",
                ["GuiWpfTipKbdBreathe"] = "Arka ışık aşağıda ayarlanan renklerde sönüp yeniden parlar.",
                ["GuiWpfTipKbdSpeed"] = "Hareketli bir efektin ne kadar hızlı çalışacağı. Yalnızca böyle bir efekt çalışırken görünür.",
                ["GuiWpfTipKbdPreset"] = "Bu kayıtlı renk takımını bölgelere uygular. Hazır ayarlar yapılandırma dosyasında tutulur.",
                ["GuiWpfTipKbdIdle"] = "Bu süre boyunca tuşa basılmazsa arka ışığı kapatır. Sıfırda kendiliğinden hiç kapanmaz.",

                // Günlük sayfası.
                ["GuiWpfTipPauseSwitch"] = "Siz okurken yeni kayıtların akmasını durdurur. Hiçbir şey kaybolmaz; geri açıldığında görünürler.",
                ["GuiWpfTipLogExport"] = "Şu an görünen her şeyi bir metin dosyasına kaydeder.",
                ["GuiWpfTipLogClear"] = "Listeyi boşaltır. Günlük dosyaya yazılıyorsa dosyaya dokunmaz.",
                ["GuiWpfTipFilterProblems"] = "Yalnızca uyarılar ve hatalar — neyin ters gittiği.",
                ["GuiWpfTipFilterHardware"] = "Uygulamanın makineye ne sorduğu ve makinenin ne yanıtladığı.",
                ["GuiWpfTipFilterInterface"] = "Pencerede ve tepsi menüsünde ne yapıldığı.",
                ["GuiWpfTipFilterBios"] = "Tek tek ürün yazılımı çağrıları. Gürültülüdür; bir sorun araştırılmıyorsa kapalı tutulur.",
                ["GuiWpfTipFilterEc"] = "Gömülü Denetleyici'ye tek tek okuma ve yazmalar. Daha da gürültülüdür.",
                ["GuiWpfTipLogSearch"] = "Yalnızca bu metni içeren kayıtları gösterir.",
                ["GuiWpfTipLogList"] = "Uygulamanın neler yaptığı; en yenisi en altta. Söyleyecek daha çok şeyi olan kayıt, üstüne gelince onu gösterir.",

                // Ayarlar sayfası. Etiketler denetimin ne yaptığını söyler;
                // buradakiler neye mal olduğunu ya da ne işe yaradığını.
                ["GuiWpfTipOptimus"] = "Görüntü tümleşik ekran biriminden geçer, NVIDIA çipi kimse istemedikçe boşta bekler. Varsayılan olan ve pilde dayanan seçenek.",
                ["GuiWpfTipDiscrete"] = "Görüntü doğrudan NVIDIA çipine bağlanır. Daha fazla performans, gözle görülür biçimde daha az pil ve yeniden başlatma gerektirir.",
                ["GuiWpfTipBoostOff"] = "İşlemci temel frekansında tutulur. En serin ve en sessiz, aynı zamanda en yavaş.",
                ["GuiWpfTipBoostOn"] = "İşlemci ısıl payı olduğunda temel hızının üstüne çıkar. Olağan ayar.",
                ["GuiWpfTipBoostAggressive"] = "İşlemci daha sert ve daha uzun süre yükselir. Daha sıcak olur, fanlar da peşinden gelir.",
                ["GuiWpfTipBrightness"] = "Ekran arka ışığı; işlev tuşlarındaki denetimin aynısı.",
                ["GuiWpfTipThreshold"] = "Fanların azamiye zorlanacağı sıcaklık. Düşük değer daha erken tepki verir ve daha gürültülü çalışır.",
                ["GuiWpfTipFanFloor"] = "Sürgülerin ve eğrinin isteyeceği en düşük seviye. Sıfırın üstünde olması fanların hep dönmesini sağlar.",
                ["GuiWpfTipFanAutoDetect"] = "Tavan yalnızca yükselir: fanlar kayıtlı tavanın üstünde görülürse tavan yükseltilir, hiçbir şey onu geri indirmez.",
                ["GuiWpfTipSuspendOnSleep"] = "Uyku boyunca çalışır bırakılan bir program, makine uyumadan önceki sıcaklığa göre davranarak uyanır. Bu seçenek onu durdurur ve dönüşte yeniden başlatır.",
                ["GuiWpfTipStartWithWindows"] = "Uygulamanın oturum açılışında yükseltilmiş olarak başlaması için bir zamanlanmış görev kaydeder. Donanıma erişebilmesi zaten bu yükseltmeye bağlıdır.",
                ["GuiWpfTipApplyOnStart"] = "Açılışta kayıtlı fan, ekran kartı ve klavye ayarlarını yeniden uygular; ürün yazılımının bıraktığı duruma razı olmaz.",
                ["GuiWpfTipCloseExits"] = "Bu kapalıyken kapat düğmesi pencereyi gizler ve uygulama bildirim alanında çalışmayı sürdürür.",
                ["GuiWpfTipStayOnTop"] = "Pencereyi diğer pencerelerin üstünde tutar.",
                ["GuiWpfTipThrottleNotify"] = "İşlemci ısı yüzünden kısıtlandığında bildirim gösterir. Beş dakikada en çok bir kez.",
                ["GuiWpfTipPollGpu"] = "NVIDIA çipini okumak onu uyandırır. Pildeyken bu, kimsenin bakmadığı değerler için güç harcamak demektir.",
                ["GuiWpfTipFourZone"] = "Bu, donanım yazılımına sorulamaz: renk tablosu klavye ne olursa olsun dört girdiliktir, bu yüzden tek bölgeli bir klavye de dört bölgeli bir klavye gibi dört bildirir. Kapalıyken tüm klavye tek renk alır ve bu her klavyede doğru çalışır. Yalnızca klavyeniz gerçekten dört ayrı bölge hâlinde yanıyorsa açın.",
                ["GuiWpfTipRefreshFollows"] = "Güç kaynağı her değiştiğinde aşağıdaki iki hızı kendiliğinden uygular.",
                ["GuiWpfTipRefreshHigh"] = "Prizde kullanılacak hız.",
                ["GuiWpfTipRefreshLow"] = "Pilde kullanılacak hız.",
                ["GuiWpfTipRefreshAuto"] = "İki hızı da yukarıya yazılan değerler yerine ekranın yapabildiğini bildirdiği değerlerden alır.",
                ["GuiWpfTipHotkeyClear"] = "Kısayolu kaldırır. Yenisi atanana kadar hiçbir şey kaydedilmez.",
                ["GuiWpfTipHotkeyCapture"] = "Tıklayın, sonra istediğiniz kombinasyona basın. Tek başına bir tuş kabul edilmez, Escape vazgeçer.",
                ["GuiWpfTipKeyToggles"] = "Tuş fan programını başlatır, yeniden basıldığında durdurur.",
                ["GuiWpfTipKeyCycles"] = "Her basış tek bir programı açıp kapatmak yerine kayıtlı bir sonraki programa geçer.",
                ["GuiWpfTipKeyShowsFirst"] = "İlk basış pencereyi açar; ikinci basıştan itibaren tuş olağan işini yapar.",
                ["GuiWpfTipKeySilent"] = "Program değiştiğinde bildirim gösterilmez.",
                ["GuiWpfTipKeyRuns"] = "Bunun yerine aşağıdaki komutu çalıştırır. Bu, fan programının önüne geçer.",
                ["GuiWpfTipKeyMinimised"] = "Komutu penceresi simge durumunda başlatır.",
                ["GuiWpfTipLogVerbose"] = "Donanımla yapılan her alışverişi kaydeder. Bir sorun araştırılırken açılıp sonrasında kapatılacak seçenek.",
                ["GuiWpfTipLogToFile"] = "Günlüğü ayrıca çalıştırılabilir dosyanın yanındaki bir dosyaya yazar; böylece uygulama kapansa da kalır.",
                ["GuiWpfTipReportBiosErrors"] = "Bu makinenin reddettiği ürün yazılımı çağrılarını kaydeder. Çoğu makine birkaçını reddeder ve bu bir arıza değildir.",
                ["GuiWpfTipLogFileSize"] = "Günlük dosyası bu boyuta ulaşınca baştan başlar.",
                ["GuiWpfTipCadenceMonitor"] = "Pencere açıkken donanımın ne sıklıkta okunacağı.",
                ["GuiWpfTipCadenceRecord"] = "Pencere gizliyken donanımın ne sıklıkta okunacağı. Uzun aralık daha az güç harcar.",
                ["GuiWpfTipCadenceProgram"] = "Çalışan bir fan programının sıcaklığa ne sıklıkta yeniden bakıp seviyeyi değiştireceği.",

                // Kabuk. Sekmeler kendi adlarını zaten taşıyor; buradakiler
                // üstündeki kelimeyi yinelemek yerine arkasında ne olduğunu söyler.
                ["GuiWpfTipNavDashboard"] = "Öne çıkan ölçümler, geçmiş grafiği ve fan denetimleri.",
                ["GuiWpfTipNavSensors"] = "Makinenin yayımladığı bütün ölçümler, eksiksiz.",
                ["GuiWpfTipNavCooling"] = "Fan eğrisi düzenleyicisi ve kayıtlı fan programları.",
                ["GuiWpfTipNavKeyboard"] = "Arka ışık, renkler ve efektler.",
                ["GuiWpfTipNavSystem"] = "Bu makinenin ne olduğu, neyi desteklediği ve ürün yazılımı ayarları.",
                ["GuiWpfTipNavLog"] = "Uygulamanın ne yaptığı ve donanımın ne yanıtladığı.",
                ["GuiWpfTipMinimise"] = "Pencereyi simge durumuna küçültür.",
                ["GuiWpfTipClose"] = "Pencereyi kapatır. Çıkış yapacak şekilde ayarlanmadıysa uygulama bildirim alanında çalışmayı sürdürür.",
                ["GuiWpfTipStripCpu"] = "İşlemci sıcaklığı ve son bir dakikadaki eğilimi.",
                ["GuiWpfTipStripGpu"] = "Ekran kartı sıcaklığı ve son bir dakikadaki eğilimi.",
                ["GuiWpfTipStripFan"] = "Her iki fan da donanım azamisinin yüzdesi olarak.",
                ["GuiWpfTipCardToggle"] = "Bu bölümü gösterir ya da gizler.",
                ["GuiWpfTipLegend"] = "Bu seriyi grafikte gösterir ya da gizler. Arkasındaki geçmiş her iki durumda da kaydedilmeye devam eder.",

                // Soğutma sayfası.
                ["GuiWpfTipCurveReset"] = "Eğriyi başlangıçtaki biçimine döndürür.",
                ["GuiWpfTipCurveStop"] = "Eğriyi durdurur ve fanları ürün yazılımına geri verir.",
                ["GuiWpfTipCurveApply"] = "Eğriyi çizildiği hâliyle çalıştırır; program olarak kaydetmez.",
                ["GuiWpfTipProgramList"] = "Yapılandırma dosyasında kayıtlı programlar. Birini seçmek onu yukarıdaki eğride çizer.",
                ["GuiWpfTipProgramStop"] = "Çalışan programı durdurur ve fanları ürün yazılımına geri verir.",
                ["GuiWpfTipProgramName"] = "Eğrinin kaydedileceği ad. Aynı adlı bir program varsa değiştirilir.",

                // Sistem sayfası.
                ["GuiWpfTipPowerModeSaver"] = "Windows, pil daha uzun gitsin diye makineyi geride tutar.",
                ["GuiWpfTipPowerModeBalanced"] = "Windows duruma göre kendi karar verir. Varsayılan.",
                ["GuiWpfTipPowerModeHigh"] = "Windows makineyi geride tutmayı bırakır; karşılığında güç ve ısı artar.",
                ["GuiWpfTipCopyReport"] = "Raporun tamamını panoya kopyalar.",
                ["GuiWpfBatCycles"] = "Döngü",
                ["GuiWpfBatCapacity"] = "Kapasite",

                // Sensör satırı ipuçları — her okuma üzerine gelince ne olduğunu söyler
                ["GuiTipCpuTemp"] = "İşlemci paket sıcaklığı; çipin kendi iç sensöründen okunur.",
                ["GuiTipCpuLoad"] = "Tüm mantıksal çekirdeklerde işlemcinin ne kadar meşgul olduğu; Windows'un bildirdiği değer.",
                ["GuiTipCpuPower"] = "İşlemci paketinin şu an çektiği güç; Intel RAPL üzerinden ölçülür.",
                ["GuiTipCpuLimit"] = "Ürün yazılımının işlemciyi tuttuğu güç bütçeleri: sürekli sınır (PL1) ve kısa patlama sınırı (PL2).",
                ["GuiTipCpuClock"] = "Etkin çekirdeklerin çalıştığı ortalama frekans; performans sayaçlarından.",
                ["GuiTipThrottle"] = "İşlemcinin kısılıp kısılmadığı ve neyle — ısı mı, güç sınırı mı — yoksa hiç mi.",
                ["GuiTipCores"] = "En sıcak mantıksal çekirdek ve toplam sayısı; gösterge panelindeki şerit her birini gösterir.",
                ["GuiTipGpuTemp"] = "Ekran kartı çip sıcaklığı; NVIDIA sürücüsü üzerinden okunur.",
                ["GuiTipGpuLoad"] = "Ekran kartının şu an ne kadar meşgul olduğu.",
                ["GuiTipGpuPower"] = "Ekran kartının çektiği kart gücü; NVML üzerinden ölçülür.",
                ["GuiTipGpuLimit"] = "Sürücünün karta uyguladığı güç sınırı — canlı TGP; performans profili bunu yükseltip düşürebilir.",
                ["GuiTipGpuClock"] = "Ekran kartı çekirdeğinin şu anki frekansı.",
                ["GuiTipVram"] = "Kullanımdaki adanmış görüntü belleği; karttaki toplamın içinden.",
                ["GuiTipMemLoad"] = "Fiziksel belleğin ne kadarının kullanımda olduğu.",
                ["GuiTipMemUsed"] = "Kullanımdaki fiziksel bellek; kurulu toplamın içinden.",
                ["GuiTipDisk"] = "Windows'un başlatıldığı sürücünün sıcaklığı; NVMe sağlık günlüğünden okunur.",
                ["GuiTipWifi"] = "Bağlı olduğun kablosuz ağ ve sinyal gücü.",
                ["GuiTipLink"] = "Anlaşılan kablosuz bağlantı hızı: önce alma, sonra gönderme.",
                ["GuiTipBatHealth"] = "Pilin tam şarj kapasitesinin tasarım kapasitesine oranı — ne kadar yıprandığı.",
                ["GuiTipBatCycles"] = "Pilin geçirdiği tam şarj döngüsü sayısı.",
                ["GuiTipBatCapacity"] = "Pilin tam dolduğunda tuttuğu enerji; tasarım kapasitesine karşı.",
                ["GuiTipBatRemaining"] = "Mevcut şarjda tahmini kalan süre.",
                ["GuiTipBatDraw"] = "Pilin şu an ne kadar hızlı şarj olduğu veya boşaldığı.",
                ["GuiTipBatState"] = "Makinenin prizde mi, pilde mi, yoksa şarjda mı olduğu.",
                ["GuiTipModel"] = "Makinenin pazarlama adı; kapağında ve destek çağrısında görünen isim.",
                ["GuiTipBios"] = "Makinenin çalıştırdığı ürün yazılımı sürümü.",
                ["GuiTipPlan"] = "Etkin Windows güç planı.",

                // Settings section — hardware controls
                ["GuiWpfSettings"] = "Ayarlar",
                ["GuiWpfSettingsCaption"] = "DONANIM KONTROLLERİ",
                ["GuiWpfSettingsHint"] = "Bu makinenin sunduğu kontroller. Ürün yazılımının sunmadığı bir kontrol devre dışı gösterilir.",
                ["GuiWpfGpuMode"] = "EKRAN KARTI MODU",
                ["GuiWpfGpuModeHint"] = "Ayrık mod, ekranı doğrudan NVIDIA GPU'ya bağlayarak pil ömrü pahasına daha fazla performans verir. Yeniden başlatmadan sonra etkinleşir.",
                ["GuiWpfOptimus"] = "Optimus",
                ["GuiWpfDiscrete"] = "Ayrık",
                ["GuiWpfBoost"] = "CPU TURBO BOOST",
                ["GuiWpfBoostHint"] = "İşlemcinin temel hızının üzerine çıkmasına izin verir. Daha serin ve sessiz çalışmak için kısın.",
                ["GuiWpfBoostOff"] = "Kapalı",
                ["GuiWpfBoostOn"] = "Açık",
                ["GuiWpfBoostAggressive"] = "Agresif",
                ["GuiWpfBrightness"] = "EKRAN PARLAKLIĞI",
                ["GuiWpfRestartNeeded"] = "Uygulandı — etkinleşmesi için yeniden başlatın",

                // Capability names, for the About panel's support table
                ["GuiCapKbdBacklight"] = "Klavye arka ışığı (BIOS)",
                ["GuiCapKbdColor"] = "Klavye ışık rengi",
                ["GuiCapGpuModeSwitch"] = "GPU modu değiştirme (MUX)",
                ["GuiCapGpuPower"] = "GPU güç seviyesi (Özel TGP / PPAB)",
                ["GuiCapAdapter"] = "Akıllı güç adaptörü durumu",
                ["GuiCapBornDate"] = "Üretim tarihi",
                ["GuiCapFanSpeed"] = "Fan hızı okuma (EC)",
                ["GuiCapMaxFan"] = "Azami fan modu (BIOS)",
                ["GuiCapFanLevel"] = "Fan seviyesi denetimi (BIOS)",
                ["GuiCapFanTable"] = "Fan hız tablosu (BIOS)",
                ["GuiCapBiosTemp"] = "BIOS sıcaklık sensörü",
                ["GuiCapBiosThrottle"] = "BIOS kısıtlama durumu",
                ["GuiCapMemOc"] = "Bellek hız aşırtma (XMP)",
                ["GuiCapUndervolt"] = "Düşük voltaj desteği (BIOS)",
                ["GuiCapLedAnim"] = "LED animasyon tablosu",
                ["GuiCapCpuMsr"] = "CPU sıcaklığı (MSR)",
                ["GuiCapCpuRapl"] = "CPU güç / saat (RAPL)",
                ["GuiCapCpuCores"] = "Çekirdek başına sıcaklık",
                ["GuiCapCpuBoost"] = "CPU Turbo Boost denetimi",
                ["GuiCapNvapi"] = "NVIDIA GPU izleme (NVAPI)",
                ["GuiCapNvml"] = "GPU güç çekişi (NVML)",
                ["GuiCapBrightness"] = "Ekran parlaklığı denetimi",
                ["GuiCapPowerMode"] = "Windows güç modu değiştirme",
                ["GuiCapDiskTemp"] = "NVMe sürücü sıcaklığı",
                ["GuiCapWifi"] = "Wi-Fi sinyal / SSID (bağlıyken)",
                ["GuiCapBatteryHealth"] = "Pil sağlığı / şarj döngüleri",
                ["GuiCapZones4"] = "4 bölge",
                ["GuiCapZone1"] = "tek bölge",

                // Units
                ["UnitFrequency"] = "Hz",
                ["UnitPercent"] = "%",
                ["UnitPower"] = "W",
                ["UnitRotationRate"] = "rpm",
                ["UnitRotationRate_CustomFont"] = Conv.GetChar(Conv.SpecialChar.Prime1) + Conv.GetChar(Conv.SpecialChar.SupMinus) + Conv.GetChar(Conv.SpecialChar.Sup1),
                ["UnitTemperature"] = "°C",
                ["UnitTemperature_CustomFont"] = Conv.GetChar(Conv.SpecialChar.DegreeCelsius),
                ["UnitTimeSecond_CustomFont"] = Conv.GetChar(Conv.SpecialChar.SpacePerEm6) + Conv.GetChar(Conv.SpecialChar.Prime2),

                // Language identifier
                ["_Language"] = "Turkish"

            };

    }

}
