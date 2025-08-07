//DONE: Make a codeblock sequencer.
//DONE: Make sure SOI maps are detected correctly (see screenshot of 4 axis maps issue)
//DONE: Make filelength a variable
//DONE: Axis collection, browser and editor support (refresh open maps after editing an axis)
//DONE: Bugfix, create new project causes System.UnauthorizedAccessException because of default path to Application.StartupPath
//DONE: Compare should not show infobox about missing axis
//DONE: Compare should use correct file for axis and addresses
//DONE: Add axis units to mapviewer
//DONE: Dynograph (torque(Nm) = IQ * 6
//DONE: Add support for flipping maps upside down
//DONE: Find launch control map (limiter actually) 0x4c640, corrupt axis starts with 0x80 0x00 0x00 0x0A
//DONE: Add checksum support
//DONE: axis change in compare mode?
//DONE: undo button in mapviewer (read from file?)
//DONE: EDC15V: sometimes also has three repeating smoke maps (watch the index IDs, they might be shuffled)
/*DONE: Additional information about codeblocks AG/SG indicator
G038906019HJ 1,9l R4 EDC  SG  5259 39S03217 0281010977 FFEWC400 038906019HJ 05/03 <-- Standard Getriebe codeblock 2 (normally manual)
G038906019HJ 1,9l R4 EDC  SG  5259 39S03217 0281010977 FFEWE400 038906019HJ 05/03 <-- Standard Getriebe codeblock 3 (normally 4motion)
G038906019HJ 1,9l R4 EDC  AG  5259 39S03217 0281010977 FFEWB400 038906019HJ 05/03 <-- Automat  Getriebe codeblock 1 (normally automatic)
 *                        ^                                ^
 * */
//DONE: ALSO use codeblock offset in codeblock synchronization option


//TEST: checksum issue with the ARL file (different structure), done for test!
//TEST: Userdescription for symbols + XML import/export
//HOLD: Find RPM limiter (ASZ @541A2 @641A2 and @741A2 value = 0x14B4 = 5300 rpm) <-- not very wise
//TEST: EDC15V: Don't forget the checksum for 1MB files
//TEST: EDC15V: Detect and fill codeBlocks
//HOLD: codeblocks: unreliable

//TODO: Rewrite XDF interface to TunerPro V5 XML specs (not documented...)
//TODO: Add EEPROM read/write support (K-line)
//TODO: KWP1281 support (K-line, slow init)
//TODO: Add EDC15P support ... 85%  LoHi
//TODO: Add EDC15V support ... 70%  LoHi 1MB, 512 Kb    (IMPROVE SUPPORT + CHECKSUM)
//TODO: Add EDC15M support ... 10%  LoHi (Opel?)        (CHECKSUM)
//TODO: Add EDC15C support ... 1%   LoHi                (CHECKSUM) 
//TODO: Add EDC16x support ... 5%   HiLo                (CHECKSUM)
//TODO: Add MSA15  support ... 25%  LoHi                (CHECKSUM)
//TODO: Add MSA6   support ... 0%   8-bit               (CHECKSUM) length indicators only (like ME7 and EDC16)
//TODO: Add EDC17  support ... 1%                       (CHECKSUM)
//TODO: Add major program switches (MAP/MAF switch etc)
//TODO: Add better hex-diff viewer (HexWorkShop example)
//TODO: Compressormap plotter with GT17 and most used upgrade turbo maps, boost/airmass from comperssor map
//TODO: copy from excel into mapviewer
//TODO: A2L/Damos import
//TODO: EDC15V: Don't forget the checksum for 256kB files
//TODO: Check older EDC15V-5 files.. these seem to be different
//TODO: Checksums: determine type on file open and count how many correct in stead of how many false? 12 banks in edc15v?
//TODO: Smoke limiter repeater is seen as a map (3x13) this is incorrect. Fix please vw passat bin @4DC72. 
//      (remember issue with len2Skip, we loose SOI limiter)
//TODO: make partnumber/swid editable

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.IO;
using DevExpress.XtraBars.Docking;
using DevExpress.XtraBars;
using DevExpress.Skins;
using DevExpress.XtraEditors.Controls;
using DevExpress.XtraEditors;
using System.Security.Cryptography;
using System.Xml;
using Newtonsoft.Json;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text.RegularExpressions;
using static DevExpress.Charts.Native.SideBySideBarInteractionAlgorithm;
using System.Net;

namespace VAGSuite
{

    public delegate void DelegateStartReleaseNotePanel(string filename, string version);

    public enum GearboxType : int
    {
        Automatic,
        Manual,
        FourWheelDrive
    }

    public enum EDCFileType : int
    {
        EDC15P,
        EDC15P6, // different map structure
        EDC15V,
        EDC15M,
        EDC15C,
        EDC16,
        EDC17,  // 512Kb/2048Kb
        MSA6,
        MSA11,
        MSA12,
        MSA15,
        Unknown
    }

    public enum EngineType : int
    {
        cc1200,
        cc1400,
        cc1600,
        cc1900,
        cc2500
    }

    public enum ImportFileType : int
    {
        XML,
        A2L,
        CSV,
        AS2,
        Damos
    }

    public partial class frmMain : DevExpress.XtraEditors.XtraForm
    {
        private AppSettings m_appSettings;
        private msiupdater m_msiUpdater;
        private bool TableViewerStarted = false;

        private string currentBinFilePath;
        // Variables globales pour suivre l'état
        private bool isFAPEnabled = false;
        private bool isStartStopEnabled = false;
        private bool isFileLoaded = false;

        public DelegateStartReleaseNotePanel m_DelegateStartReleaseNotePanel;
        private frmSplash splash;

        // Variables pour stocker les informations d'ECU actuelles
        private string _currentECUType = "";
        private string _currentCarMake = "";
        private Dictionary<string, Dictionary<string, MapDefinition>> _mapDefinitions;

        public frmMain()
        {
            try
            {
                splash = new frmSplash();
                splash.Show();
                Application.DoEvents();
            }
            catch (Exception)
            {

            }
                
            InitializeComponent();
            try
            {
                m_DelegateStartReleaseNotePanel = new DelegateStartReleaseNotePanel(this.StartReleaseNotesViewer);
            }
            catch (Exception E)
            {
                Console.WriteLine(E.Message);
            }

        }


        private void StartReleaseNotesViewer(string xmlfilename, string version)
        {
            dockManager1.BeginUpdate();
            DockPanel dp = dockManager1.AddPanel(DockingStyle.Right);
            dp.ClosedPanel += new DockPanelEventHandler(dockPanel_ClosedPanel);
            dp.Tag = xmlfilename;
            ctrlReleaseNotes mv = new ctrlReleaseNotes();
            mv.LoadXML(xmlfilename);
            mv.Dock = DockStyle.Fill;
            dp.Width = 500;
            dp.Text = "Release notes: " + version;
            dp.Controls.Add(mv);
            dockManager1.EndUpdate();
        }

        private void btnBinaryCompare_ItemClick(object sender, DevExpress.XtraBars.ItemClickEventArgs e)
        {
            if (Tools.Instance.m_currentfile != "")
            {
                OpenFileDialog openFileDialog1 = new OpenFileDialog();
                //openFileDialog1.Filter = "Binaries|*.bin;*.ori";
                openFileDialog1.Multiselect = false;
                if (openFileDialog1.ShowDialog() == DialogResult.OK)
                {
                    frmBinCompare bincomp = new frmBinCompare();
                    bincomp.Symbols = Tools.Instance.m_symbols;
                    bincomp.SetCurrentFilename(Tools.Instance.m_currentfile);
                    bincomp.SetCompareFilename(openFileDialog1.FileName);
                    bincomp.CompareFiles();
                    bincomp.ShowDialog();
                }
            }
            else
            {
                MessageBox.Show("No file is currently opened, you need to open a binary file first to compare it to another one!");
            }
        }

        private void btnOpenFile_ItemClick(object sender, DevExpress.XtraBars.ItemClickEventArgs e)
        {
            //OpenFileDialog openFileDialog3 = new OpenFileDialog
            //{
            //    Filter = "Fichiers BIN (*.bin)|*.bin",
            //    Title = "Sélectionner un fichier ECU"
            //};
            OpenFileDialog openFileDialog1 = new OpenFileDialog();
            //openFileDialog1.Filter = "Binaries|*.bin;*.ori";
            openFileDialog1.Multiselect = false;
            if (openFileDialog1.ShowDialog() == DialogResult.OK)
            {
                CloseProject();
                m_appSettings.Lastprojectname = "";
                OpenFile(openFileDialog1.FileName, true);
                m_appSettings.LastOpenedType = 0;
                currentBinFilePath = openFileDialog1.FileName;

                // Charger le fichier binaire pour l'analyse
                try
                {
                    byte[] fileBytes = File.ReadAllBytes(currentBinFilePath);

                    // Vérifier si le véhicule est équipé d'un FAP et son état
                    DetectFAPStatus(fileBytes);

                    // Vérifier l'état du Start & Stop
                    DetectStartStopStatus(fileBytes);

                    isFileLoaded = true;

                    // Mettre à jour l'interface en fonction des états détectés
                    UpdateButtonStates();
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Erreur lors de l'analyse du fichier : " + ex.Message, "Erreur", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }


        private void OpenFile(string fileName, bool showMessage)
        {
            // don't allow multiple instances
            lock (this)
            {
                btnOpenFile.Enabled = false;
                btnOpenProject.Enabled = false;
                try
                {

                    Tools.Instance.m_currentfile = fileName;
                    FileInfo fi = new FileInfo(fileName);
                    Tools.Instance.m_currentfilelength = (int)fi.Length;
                    try
                    {
                        fi.IsReadOnly = false;
                        barReadOnly.Caption = "Ok";
                    }
                    catch (Exception E)
                    {
                        Console.WriteLine("Failed to remove read only flag: " + E.Message);
                        barReadOnly.Caption = "File is READ ONLY";
                    }
                    this.Text = "VAGEDCSuite [ " + Path.GetFileName(Tools.Instance.m_currentfile) + " ]";
                    Tools.Instance.m_symbols = new SymbolCollection();
                    Tools.Instance.codeBlockList = new List<CodeBlock>();
                    barFilenameText.Caption = Path.GetFileName(fileName);

                    Tools.Instance.m_symbols = DetectMaps(Tools.Instance.m_currentfile, out Tools.Instance.codeBlockList, out Tools.Instance.AxisList, showMessage, true);

                    LoadSymbolsFromBinaryFile(fileName, Tools.Instance.m_symbols);

                    gridControl1.DataSource = null;
                    Application.DoEvents();
                    gridControl1.DataSource = Tools.Instance.m_symbols;
                    //gridViewSymbols.BestFitColumns();
                    Application.DoEvents();
                    try
                    {
                        gridViewSymbols.ExpandAllGroups();
                    }
                    catch (Exception)
                    {

                    }
                    m_appSettings.Lastfilename = Tools.Instance.m_currentfile;
                    VerifyChecksum(fileName, !m_appSettings.AutoChecksum, false);

                    TryToLoadAdditionalSymbols(fileName, ImportFileType.XML, Tools.Instance.m_symbols, true);

                }
                catch (Exception)
                {
                }
                btnOpenFile.Enabled = true;
                btnOpenProject.Enabled = true;
            }
        }




        //####################################################################################################################################################################################
        //####################################################################################################################################################################################


        #region OLD VERSION OF DETECT MAPS
        //private SymbolCollection DetectMaps(string filename, out List<CodeBlock> newCodeBlocks, out List<AxisHelper> newAxisHelpers, bool showMessage, bool isPrimaryFile)
        //{
        //    IEDCFileParser parser = Tools.Instance.GetParserForFile(filename, isPrimaryFile);
        //    newCodeBlocks = new List<CodeBlock>();
        //    newAxisHelpers = new List<AxisHelper>();
        //    SymbolCollection newSymbols = new SymbolCollection();

        //    if (parser != null)
        //    {
        //        byte[] allBytes = File.ReadAllBytes(filename);
        //        string boschnumber = parser.ExtractBoschPartnumber(allBytes);
        //        string softwareNumber = parser.ExtractSoftwareNumber(allBytes);
        //        partNumberConverter pnc = new partNumberConverter();
        //        ECUInfo info = pnc.ConvertPartnumber(boschnumber, allBytes.Length);

        //        // Ajout d'un message de débogage pour voir le type d'ECU détecté
        //        Console.WriteLine($"ECU détecté: Type={info.EcuType}, Marque={info.CarMake}, PartNumber={info.PartNumber}");

        //        // Modification: Accepter plus de types d'ECU, y compris ceux utilisés par Audi
        //        bool isAcceptedECU = info.EcuType.StartsWith("EDC15P") ||
        //                             info.EcuType.StartsWith("EDC15VM") ||
        //                             info.EcuType.StartsWith("EDC15") ||  // Pour les autres variantes EDC15
        //                             info.EcuType.StartsWith("EDC16") ||  // Pour EDC16 utilisé dans certains Audi
        //                             info.EcuType.StartsWith("ME7") ||    // Pour ME7 utilisé dans les Audi essence
        //                             info.CarMake.Contains("AUDI");       // Accepter explicitement si c'est un Audi

        //        // Afficher message d'information sans bloquer le processus
        //        if (!isAcceptedECU && info.EcuType != string.Empty && showMessage)
        //        {
        //            // Changer le message pour être informatif plutôt que bloquant
        //            Console.WriteLine($"Note: Type d'ECU non standard détecté [{info.EcuType}] dans {Path.GetFileName(filename)}. Tentative de traitement quand même.");
        //            // Optionnel: Afficher un message mais continuer le traitement
        //            if (showMessage)
        //            {
        //                frmInfoBox infobx = new frmInfoBox($"Type d'ECU non standard [{info.EcuType}] détecté. Le programme essaiera de traiter ce fichier quand même.");
        //            }
        //        }

        //        if (info.EcuType == string.Empty)
        //        {
        //            Console.WriteLine("Numéro de pièce " + info.PartNumber + " inconnu " + filename);
        //        }

        //        if (isPrimaryFile)
        //        {
        //            string partNo = parser.ExtractPartnumber(allBytes);
        //            partNo = Tools.Instance.StripNonAscii(partNo);
        //            softwareNumber = Tools.Instance.StripNonAscii(softwareNumber);
        //            barPartnumber.Caption = partNo + " " + softwareNumber;

        //            // Ajout d'information Audi-spécifique si détecté
        //            string carInfo = info.CarMake;
        //            if (info.CarMake.Contains("AUDI") || boschnumber.Contains("AUDI"))
        //            {
        //                carInfo = "AUDI " + carInfo;
        //            }
        //            barAdditionalInfo.Caption = info.PartNumber + " " + carInfo + " " + info.EcuType + " " + parser.ExtractInfo(allBytes);
        //        }

        //        // Toujours essayer de parser le fichier, même pour les ECU non standard
        //        newSymbols = parser.parseFile(filename, out newCodeBlocks, out newAxisHelpers);
        //        newSymbols.SortColumn = "Flash_start_address";
        //        newSymbols.SortingOrder = GenericComparer.SortOrder.Ascending;
        //        newSymbols.Sort();
        //        //parser.NameKnownMaps(allBytes, newSymbols, newCodeBlocks);
        //        //parser.FindSVBL(allBytes, filename, newSymbols, newCodeBlocks);
        //        /*SymbolTranslator strans = new SymbolTranslator();
        //        foreach (SymbolHelper sh in newSymbols)
        //        {
        //            sh.Description = strans.TranslateSymbolToHelpText(sh.Varname);
        //        }*/
        //        // check for must have maps... if there are maps missing, report it
        //        // Vérification des cartes manquantes avec ajustement pour Audi
        //        if (showMessage && (parser is EDC15PFileParser || parser is EDC15P6FileParser || info.CarMake.Contains("AUDI")))
        //        {
        //            string _message = string.Empty;
        //            if (MapsWithNameMissing("EGR", newSymbols)) _message += "EGR maps missing" + Environment.NewLine;
        //            if (MapsWithNameMissing("SVBL", newSymbols)) _message += "SVBL missing" + Environment.NewLine;
        //            if (MapsWithNameMissing("Torque limiter", newSymbols)) _message += "Torque limiter missing" + Environment.NewLine;
        //            if (MapsWithNameMissing("Smoke limiter", newSymbols)) _message += "Smoke limiter missing" + Environment.NewLine;
        //            //if (MapsWithNameMissing("IQ by MAF limiter", newSymbols)) _message += "IQ by MAF limiter missing" + Environment.NewLine;
        //            if (MapsWithNameMissing("Injector duration", newSymbols)) _message += "Injector duration maps missing" + Environment.NewLine;
        //            if (MapsWithNameMissing("Start of injection", newSymbols)) _message += "Start of injection maps missing" + Environment.NewLine;
        //            if (MapsWithNameMissing("N75 duty cycle", newSymbols)) _message += "N75 duty cycle map missing" + Environment.NewLine;
        //            if (MapsWithNameMissing("Inverse driver wish", newSymbols)) _message += "Inverse driver wish map missing" + Environment.NewLine;
        //            if (MapsWithNameMissing("Boost target map", newSymbols)) _message += "Boost target map missing" + Environment.NewLine;
        //            if (MapsWithNameMissing("SOI limiter", newSymbols)) _message += "SOI limiter missing" + Environment.NewLine;
        //            if (MapsWithNameMissing("Driver wish", newSymbols)) _message += "Driver wish map missing" + Environment.NewLine;
        //            if (MapsWithNameMissing("Boost limit map", newSymbols)) _message += "Boost limit map missing" + Environment.NewLine;

        //            if (MapsWithNameMissing("MAF correction", newSymbols)) _message += "MAF correction map missing" + Environment.NewLine;
        //            if (MapsWithNameMissing("MAF linearization", newSymbols)) _message += "MAF linearization map missing" + Environment.NewLine;
        //            if (MapsWithNameMissing("MAP linearization", newSymbols)) _message += "MAP linearization map missing" + Environment.NewLine;
        //            if (_message != string.Empty)
        //            {
        //                frmInfoBox infobx = new frmInfoBox(_message);
        //            }
        //        }

        //        if (MapsWithNameMissing("Driver wish", newSymbols))
        //        {
        //            string _message = string.Empty;
        //            // Chercher les alternatives Audi pour Driver wish
        //            if (MapsWithNameMissing("Driver_wish", newSymbols) &&
        //                MapsWithNameMissing("DriverWish", newSymbols) &&
        //                MapsWithNameMissing("Audi driver wish", newSymbols))
        //            {
        //                _message += "Driver wish map missing" + Environment.NewLine;
        //            }
        //        }
        //        if (isPrimaryFile)
        //        {
        //            barSymCount.Caption = newSymbols.Count.ToString() + " symbols";

        //            if (MapsWithNameMissing("Launch control map", newSymbols))
        //            {
        //                btnActivateLaunchControl.Enabled = true;
        //            }
        //            else
        //            {
        //                btnActivateLaunchControl.Enabled = false;
        //            }
        //            btnActivateSmokeLimiters.Enabled = false;
        //            try
        //            {
        //                if (Tools.Instance.codeBlockList.Count > 0)
        //                {
        //                    if ((GetMapCount("Smoke limiter", newSymbols) / Tools.Instance.codeBlockList.Count) == 1)
        //                    {
        //                        btnActivateSmokeLimiters.Enabled = true;
        //                    }
        //                    else
        //                    {
        //                        btnActivateSmokeLimiters.Enabled = false;
        //                    }
        //                }
        //            }
        //            catch (Exception)
        //            {

        //            }
        //        }
        //    }
        //    return newSymbols;

        //}

        #endregion

        #region
        private SymbolCollection DetectMaps(string filename, out List<CodeBlock> newCodeBlocks, out List<AxisHelper> newAxisHelpers, bool showMessage, bool isPrimaryFile)
        {
            IEDCFileParser parser = Tools.Instance.GetParserForFile(filename, isPrimaryFile);
            newCodeBlocks = new List<CodeBlock>();
            newAxisHelpers = new List<AxisHelper>();
            SymbolCollection newSymbols = new SymbolCollection();

            if (parser != null)
            {
                byte[] allBytes = File.ReadAllBytes(filename);
                string boschnumber = parser.ExtractBoschPartnumber(allBytes);
                string softwareNumber = parser.ExtractSoftwareNumber(allBytes);
                partNumberConverter pnc = new partNumberConverter();
                ECUInfo info = pnc.ConvertPartnumber(boschnumber, allBytes.Length);

                // Détection améliorée du type d'ECU
                string ecuType = DetermineECUType(allBytes, info);

                // Charge les définitions d'adresses pour cette marque et ce type d'ECU
                LoadAddressDefinitions(ecuType, info.CarMake);

                if (isPrimaryFile)
                {
                    string partNo = parser.ExtractPartnumber(allBytes);
                    partNo = Tools.Instance.StripNonAscii(partNo);
                    softwareNumber = Tools.Instance.StripNonAscii(softwareNumber);
                    barPartnumber.Caption = partNo + " " + softwareNumber;
                    barAdditionalInfo.Caption = info.PartNumber + " " + info.CarMake + " " + ecuType + " " + parser.ExtractInfo(allBytes);
                }

                // Tente de parser le fichier
                newSymbols = parser.parseFile(filename, out newCodeBlocks, out newAxisHelpers);
                newSymbols.SortColumn = "Flash_start_address";
                newSymbols.SortingOrder = GenericComparer.SortOrder.Ascending;
                newSymbols.Sort();

                // Vérifie les cartes manquantes mais continue quand même
                if (showMessage)
                {
                    CheckMissingMaps(newSymbols, ecuType, info.CarMake);
                }

         
                if (isPrimaryFile)
                {
                    barSymCount.Caption = newSymbols.Count.ToString() + " symbols";
                    // Appel sans paramètres
                    UpdateButtonStates();
                }

            }
            if (isPrimaryFile)
            {
                LoadCreatedMaps(filename, newSymbols);
            }

            //return newSymbols;
            return newSymbols;
        }

        private void LoadCreatedMaps(string filename, SymbolCollection symbols)
        {
            // Chemin vers le fichier de métadonnées
            string metadataPath = Path.Combine(
                Path.GetDirectoryName(filename),
                Path.GetFileNameWithoutExtension(filename) + "_metadata.xml"
            );

            if (File.Exists(metadataPath))
            {
                try
                {
                    System.Data.DataTable dt = new System.Data.DataTable();
                    dt.ReadXml(metadataPath);

                    foreach (DataRow row in dt.Rows)
                    {
                        // Vérifier si cette carte existe déjà dans la collection
                        bool exists = false;
                        int address = Convert.ToInt32(row["FLASHADDRESS"]);

                        foreach (SymbolHelper existing in symbols)
                        {
                            if (existing.Flash_start_address == address)
                            {
                                exists = true;
                                break;
                            }
                        }

                        // Si la carte n'existe pas, l'ajouter
                        if (!exists)
                        {
                            SymbolHelper symbol = new SymbolHelper();
                            symbol.Varname = row["SYMBOLNAME"].ToString();
                            symbol.Flash_start_address = address;
                            symbol.Length = Convert.ToInt32(row["LENGTH"]);
                            symbol.X_axis_length = Convert.ToInt32(row["XAXISLEN"]);
                            symbol.Y_axis_length = Convert.ToInt32(row["YAXISLEN"]);

                            if (row.Table.Columns.Contains("XAXISADDR"))
                                symbol.X_axis_address = Convert.ToInt32(row["XAXISADDR"]);

                            if (row.Table.Columns.Contains("YAXISADDR"))
                                symbol.Y_axis_address = Convert.ToInt32(row["YAXISADDR"]);

                            if (row.Table.Columns.Contains("XAXISDESCR"))
                                symbol.X_axis_descr = row["XAXISDESCR"].ToString();

                            if (row.Table.Columns.Contains("YAXISDESCR"))
                                symbol.Y_axis_descr = row["YAXISDESCR"].ToString();

                            if (row.Table.Columns.Contains("ZAXISDESCR"))
                                symbol.Z_axis_descr = row["ZAXISDESCR"].ToString();

                            if (row.Table.Columns.Contains("CORRECTION"))
                                symbol.Correction = Convert.ToDouble(row["CORRECTION"]);
                            else
                                symbol.Correction = 1.0;

                            if (row.Table.Columns.Contains("OFFSET"))
                                symbol.Offset = Convert.ToDouble(row["OFFSET"]);
                            else
                                symbol.Offset = 0.0;

                            if (row.Table.Columns.Contains("CATEGORY"))
                                symbol.Category = row["CATEGORY"].ToString();

                            if (row.Table.Columns.Contains("SUBCATEGORY"))
                                symbol.Subcategory = row["SUBCATEGORY"].ToString();

                            symbols.Add(symbol);
                            Console.WriteLine("Carte créée chargée: " + symbol.Varname);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Erreur lors du chargement des cartes créées: " + ex.Message);
                }
            }
        }
        #endregion
        // ######################################################################################################################################################################################################
        private void CheckMissingMaps(SymbolCollection symbols, string ecuType, string carMake)
        {
            string _message = string.Empty;

            // Liste des cartes essentielles à vérifier
            string[] essentialMaps = new string[] {
                "Driver wish", "Torque limiter", "Smoke limiter", "Target boost",
                "Boost pressure limiter", "SVBL", "EGR", "Injector duration",
                "Start of injection", "N75 duty cycle"
            };

            // Vérification pour chaque carte
            foreach (string map in essentialMaps)
            {
                if (MapsWithNameMissing(map, symbols))
                {
                    _message += map + " maps missing" + Environment.NewLine;
                }
            }

            // Afficher message si des cartes sont manquantes
            if (_message != string.Empty)
            {
                frmInfoBox infobx = new frmInfoBox(_message);
            }
        }

        // Structure pour les définitions de cartes
        private struct MapDefinition
        {
            public int Address;
            public int Length;
            public int Columns;
            public int Rows;
            public string Description;
            public List<string> AlternativeNames;
        }

        // Charge les définitions d'adresses depuis le fichier JSON
        private void LoadAddressDefinitions(string ecuType, string carMake)
        {
            _currentECUType = ecuType;
            _currentCarMake = carMake;
            _mapDefinitions = new Dictionary<string, Dictionary<string, MapDefinition>>();

            try
            {
                string definitionsFile = Path.Combine(Application.StartupPath, "MapDefinitions.json");
                if (File.Exists(definitionsFile))
                {
                    string jsonContent = File.ReadAllText(definitionsFile);
                    _mapDefinitions = JsonConvert.DeserializeObject<Dictionary<string, Dictionary<string, MapDefinition>>>(jsonContent);
                }
                
                else
                {
                    // Créer un fichier par défaut avec les définitions VAG
                    CreateDefaultDefinitionsFile(definitionsFile);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Erreur lors du chargement des définitions: " + ex.Message);
            }
        }
        private void CreateDefaultDefinitionsFile(string filePath)
        {
            var defaultDefs = new Dictionary<string, Dictionary<string, MapDefinition>>();

            // Ajout des définitions VAG
            var vagMaps = new Dictionary<string, MapDefinition>();
            vagMaps["Driver wish"] = new MapDefinition
            {
                Address = 0x12345, // Exemple d'adresse
                Length = 128,
                Columns = 16,
                Rows = 8,
                Description = "Driver wish map",
                AlternativeNames = new List<string> { "Fahrerwunsch", "Driver_wish" }
            };
            // Ajouter d'autres cartes VAG...

            defaultDefs["EDC15P"] = vagMaps;

            // Ajout des définitions BMW
            var bmwMaps = new Dictionary<string, MapDefinition>();
            bmwMaps["Driver wish"] = new MapDefinition
            {
                Address = 0x52416,
                Length = 0x29C,
                Columns = 16,
                Rows = 12,
                Description = "Driver wish map for BMW E46",
                AlternativeNames = new List<string> { "FahrPedal_Kennfeld", "Pedal_Map" }
            };
            // Ajouter d'autres cartes BMW...

            defaultDefs["BMW_DDE4"] = bmwMaps;

            // Ajouter d'autres marques...
            // Ajout des définitions CITROËN EDC15C2
            var citroenEdc15Maps = new Dictionary<string, MapDefinition>();
            citroenEdc15Maps["Driver wish"] = new MapDefinition
            {
                Address = 0x1A240,
                Length = 512,
                Columns = 16,
                Rows = 16,
                Description = "Driver wish map for Citroën EDC15C2",
                AlternativeNames = new List<string> { "Consigne_conducteur", "Demande_conducteur", "Pedale_accelerateur" }
            };
            citroenEdc15Maps["Torque limiter"] = new MapDefinition
            {
                Address = 0x1A440,
                Length = 512,
                Columns = 16,
                Rows = 16,
                Description = "Torque limiter map for Citroën EDC15C2",
                AlternativeNames = new List<string> { "Limiteur_couple", "Couple_maximum", "Limitation_couple" }
            };
            citroenEdc15Maps["Smoke limiter"] = new MapDefinition
            {
                Address = 0x1A640,
                Length = 512,
                Columns = 16,
                Rows = 16,
                Description = "Smoke limiter map for Citroën EDC15C2",
                AlternativeNames = new List<string> { "Limiteur_fumees", "Controle_fumees", "Limiteur_opacite" }
            };
            citroenEdc15Maps["Target boost"] = new MapDefinition
            {
                Address = 0x1A840,
                Length = 512,
                Columns = 16,
                Rows = 16,
                Description = "Target boost map for Citroën EDC15C2",
                AlternativeNames = new List<string> { "Consigne_suralimentation", "Pression_suralim", "Turbo_consigne" }
            };
            citroenEdc15Maps["Injection quantity"] = new MapDefinition
            {
                Address = 0x1AA40,
                Length = 512,
                Columns = 16,
                Rows = 16,
                Description = "Injection quantity map for Citroën EDC15C2",
                AlternativeNames = new List<string> { "Quantite_injection", "Debit_injecteur", "Quantite_carburant" }
            };
            citroenEdc15Maps["Start of injection"] = new MapDefinition
            {
                Address = 0x1AC40,
                Length = 512,
                Columns = 16,
                Rows = 16,
                Description = "Start of injection map for Citroën EDC15C2",
                AlternativeNames = new List<string> { "Debut_injection", "Avance_injection", "SOI" }
            };
            citroenEdc15Maps["Rail pressure"] = new MapDefinition
            {
                Address = 0x1AE40,
                Length = 512,
                Columns = 16,
                Rows = 16,
                Description = "Rail pressure map for Citroën EDC15C2",
                AlternativeNames = new List<string> { "Pression_rampe", "Common_rail", "Pression_rail_carburant" }
            };

            defaultDefs["CITROEN_EDC15C2"] = citroenEdc15Maps;

            // Ajout des définitions CITROËN EDC16C34
            var citroenEdc16Maps = new Dictionary<string, MapDefinition>();
            citroenEdc16Maps["Driver wish"] = new MapDefinition
            {
                Address = 0x2A240,
                Length = 648,
                Columns = 18,
                Rows = 18,
                Description = "Driver wish map for Citroën EDC16C34",
                AlternativeNames = new List<string> { "Consigne_conducteur", "Demande_conducteur", "Pedale_accelerateur" }
            };
            citroenEdc16Maps["Torque limiter"] = new MapDefinition
            {
                Address = 0x2A4C0,
                Length = 648,
                Columns = 18,
                Rows = 18,
                Description = "Torque limiter map for Citroën EDC16C34",
                AlternativeNames = new List<string> { "Limiteur_couple", "Couple_maximum", "Limitation_couple" }
            };
            citroenEdc16Maps["Smoke limiter"] = new MapDefinition
            {
                Address = 0x2A740,
                Length = 648,
                Columns = 18,
                Rows = 18,
                Description = "Smoke limiter map for Citroën EDC16C34",
                AlternativeNames = new List<string> { "Limiteur_fumees", "Controle_fumees", "Limiteur_opacite" }
            };
            citroenEdc16Maps["Target boost"] = new MapDefinition
            {
                Address = 0x2A9C0,
                Length = 648,
                Columns = 18,
                Rows = 18,
                Description = "Target boost map for Citroën EDC16C34",
                AlternativeNames = new List<string> { "Consigne_suralimentation", "Pression_suralim", "Turbo_consigne" }
            };
            citroenEdc16Maps["Injection quantity"] = new MapDefinition
            {
                Address = 0x2AC40,
                Length = 648,
                Columns = 18,
                Rows = 18,
                Description = "Injection quantity map for Citroën EDC16C34",
                AlternativeNames = new List<string> { "Quantite_injection", "Debit_injecteur", "Quantite_carburant" }
            };
            citroenEdc16Maps["Start of injection"] = new MapDefinition
            {
                Address = 0x2AEC0,
                Length = 648,
                Columns = 18,
                Rows = 18,
                Description = "Start of injection map for Citroën EDC16C34",
                AlternativeNames = new List<string> { "Debut_injection", "Avance_injection", "SOI" }
            };
            citroenEdc16Maps["Rail pressure"] = new MapDefinition
            {
                Address = 0x2B140,
                Length = 648,
                Columns = 18,
                Rows = 18,
                Description = "Rail pressure map for Citroën EDC16C34",
                AlternativeNames = new List<string> { "Pression_rampe", "Common_rail", "Pression_rail_carburant" }
            };
            citroenEdc16Maps["EGR position"] = new MapDefinition
            {
                Address = 0x2B3C0,
                Length = 648,
                Columns = 18,
                Rows = 18,
                Description = "EGR position map for Citroën EDC16C34",
                AlternativeNames = new List<string> { "Position_EGR", "EGR_ouverture", "Vanne_EGR" }
            };
            citroenEdc16Maps["Turbo position"] = new MapDefinition
            {
                Address = 0x2B640,
                Length = 648,
                Columns = 18,
                Rows = 18,
                Description = "Turbo position map for Citroën EDC16C34",
                AlternativeNames = new List<string> { "Position_turbo", "Geometrie_variable", "VGT_position" }
            };

            defaultDefs["CITROEN_EDC16C34"] = citroenEdc16Maps;

            // Ajout des définitions CITROËN EDC17C60
            var citroenEdc17Maps = new Dictionary<string, MapDefinition>();
            citroenEdc17Maps["Driver wish"] = new MapDefinition
            {
                Address = 0x4A240,
                Length = 800,
                Columns = 20,
                Rows = 20,
                Description = "Driver wish map for Citroën EDC17C60",
                AlternativeNames = new List<string> { "Consigne_conducteur", "Demande_conducteur", "Pedale_accelerateur" }
            };
            citroenEdc17Maps["Torque limiter"] = new MapDefinition
            {
                Address = 0x4A540,
                Length = 800,
                Columns = 20,
                Rows = 20,
                Description = "Torque limiter map for Citroën EDC17C60",
                AlternativeNames = new List<string> { "Limiteur_couple", "Couple_maximum", "Limitation_couple" }
            };
            citroenEdc17Maps["Smoke limiter"] = new MapDefinition
            {
                Address = 0x4A840,
                Length = 800,
                Columns = 20,
                Rows = 20,
                Description = "Smoke limiter map for Citroën EDC17C60",
                AlternativeNames = new List<string> { "Limiteur_fumees", "Controle_fumees", "Limiteur_opacite" }
            };
            citroenEdc17Maps["Target boost"] = new MapDefinition
            {
                Address = 0x4AB40,
                Length = 800,
                Columns = 20,
                Rows = 20,
                Description = "Target boost map for Citroën EDC17C60",
                AlternativeNames = new List<string> { "Consigne_suralimentation", "Pression_suralim", "Turbo_consigne" }
            };
            citroenEdc17Maps["Injection quantity"] = new MapDefinition
            {
                Address = 0x4AE40,
                Length = 800,
                Columns = 20,
                Rows = 20,
                Description = "Injection quantity map for Citroën EDC17C60",
                AlternativeNames = new List<string> { "Quantite_injection", "Debit_injecteur", "Quantite_carburant" }
            };
            citroenEdc17Maps["Start of injection"] = new MapDefinition
            {
                Address = 0x4B140,
                Length = 800,
                Columns = 20,
                Rows = 20,
                Description = "Start of injection map for Citroën EDC17C60",
                AlternativeNames = new List<string> { "Debut_injection", "Avance_injection", "SOI" }
            };
            citroenEdc17Maps["Rail pressure"] = new MapDefinition
            {
                Address = 0x4B440,
                Length = 800,
                Columns = 20,
                Rows = 20,
                Description = "Rail pressure map for Citroën EDC17C60",
                AlternativeNames = new List<string> { "Pression_rampe", "Common_rail", "Pression_rail_carburant" }
            };
            citroenEdc17Maps["EGR position"] = new MapDefinition
            {
                Address = 0x4B740,
                Length = 800,
                Columns = 20,
                Rows = 20,
                Description = "EGR position map for Citroën EDC17C60",
                AlternativeNames = new List<string> { "Position_EGR", "EGR_ouverture", "Vanne_EGR" }
            };
            citroenEdc17Maps["Turbo position"] = new MapDefinition
            {
                Address = 0x4BA40,
                Length = 800,
                Columns = 20,
                Rows = 20,
                Description = "Turbo position map for Citroën EDC17C60",
                AlternativeNames = new List<string> { "Position_turbo", "Geometrie_variable", "VGT_position" }
            };
            citroenEdc17Maps["Air mass"] = new MapDefinition
            {
                Address = 0x4BD40,
                Length = 800,
                Columns = 20,
                Rows = 20,
                Description = "Air mass map for Citroën EDC17C60",
                AlternativeNames = new List<string> { "Masse_air", "Debit_air", "Mass_air_flow" }
            };
            citroenEdc17Maps["Fuel temperature"] = new MapDefinition
            {
                Address = 0x4C040,
                Length = 512,
                Columns = 16,
                Rows = 16,
                Description = "Fuel temperature map for Citroën EDC17C60",
                AlternativeNames = new List<string> { "Temperature_carburant", "Temp_gasoil", "Temperature_fuel" }
            };

            defaultDefs["CITROEN_EDC17C60"] = citroenEdc17Maps;

            // Ajout des définitions PEUGEOT EDC15C2
            var peugeotEdc15Maps = new Dictionary<string, MapDefinition>();
            peugeotEdc15Maps["Driver wish"] = new MapDefinition
            {
                Address = 0x1A240,
                Length = 512,
                Columns = 16,
                Rows = 16,
                Description = "Driver wish map for Peugeot EDC15C2",
                AlternativeNames = new List<string> { "Demande_conducteur", "Pedale_accelerateur", "Consigne_conducteur" }
            };
            peugeotEdc15Maps["Torque limiter"] = new MapDefinition
            {
                Address = 0x1A440,
                Length = 512,
                Columns = 16,
                Rows = 16,
                Description = "Torque limiter map for Peugeot EDC15C2",
                AlternativeNames = new List<string> { "Limiteur_couple", "Couple_maximum", "Limitation_couple" }
            };
            peugeotEdc15Maps["Smoke limiter"] = new MapDefinition
            {
                Address = 0x1A640,
                Length = 512,
                Columns = 16,
                Rows = 16,
                Description = "Smoke limiter map for Peugeot EDC15C2",
                AlternativeNames = new List<string> { "Limiteur_fumees", "Controle_fumees", "Limiteur_opacite" }
            };
            peugeotEdc15Maps["Target boost"] = new MapDefinition
            {
                Address = 0x1A840,
                Length = 512,
                Columns = 16,
                Rows = 16,
                Description = "Target boost map for Peugeot EDC15C2",
                AlternativeNames = new List<string> { "Consigne_suralimentation", "Pression_suralim", "Turbo_consigne" }
            };
            peugeotEdc15Maps["Injection quantity"] = new MapDefinition
            {
                Address = 0x1AA40,
                Length = 512,
                Columns = 16,
                Rows = 16,
                Description = "Injection quantity map for Peugeot EDC15C2",
                AlternativeNames = new List<string> { "Quantite_injection", "Debit_injecteur", "Quantite_carburant" }
            };

            // Ajout des définitions PEUGEOT EDC16C3 (moteurs 1.4 HDI)
            var peugeotEdc16c3Maps = new Dictionary<string, MapDefinition>();
            peugeotEdc16c3Maps["Start of injection"] = new MapDefinition
            {
                Address = 0x2ADC0,
                Length = 576,
                Columns = 16,
                Rows = 18,
                Description = "Start of injection map for Peugeot EDC16C3",
                AlternativeNames = new List<string> { "Debut_injection", "Avance_injection", "SOI" }
            };
            peugeotEdc16c3Maps["Rail pressure"] = new MapDefinition
            {
                Address = 0x2B040,
                Length = 576,
                Columns = 16,
                Rows = 18,
                Description = "Rail pressure map for Peugeot EDC16C3",
                AlternativeNames = new List<string> { "Pression_rampe", "Common_rail", "Pression_rail_carburant" }
            };
            peugeotEdc16c3Maps["EGR position"] = new MapDefinition
            {
                Address = 0x2B2C0,
                Length = 576,
                Columns = 16,
                Rows = 18,
                Description = "EGR position map for Peugeot EDC16C3",
                AlternativeNames = new List<string> { "Position_EGR", "EGR_ouverture", "Vanne_EGR" }
            };

            defaultDefs["PEUGEOT_EDC16C3"] = peugeotEdc16c3Maps;

            // Ajout des définitions PEUGEOT EDC16C34 (moteurs 1.6 HDI)
            var peugeotEdc16c34Maps = new Dictionary<string, MapDefinition>();
            peugeotEdc16c34Maps["Driver wish"] = new MapDefinition
            {
                Address = 0x2A240,
                Length = 648,
                Columns = 18,
                Rows = 18,
                Description = "Driver wish map for Peugeot EDC16C34",
                AlternativeNames = new List<string> { "Demande_conducteur", "Pedale_accelerateur", "Consigne_conducteur" }
            };
            peugeotEdc16c34Maps["Torque limiter"] = new MapDefinition
            {
                Address = 0x2A4C0,
                Length = 648,
                Columns = 18,
                Rows = 18,
                Description = "Torque limiter map for Peugeot EDC16C34",
                AlternativeNames = new List<string> { "Limiteur_couple", "Couple_maximum", "Limitation_couple" }
            };
            peugeotEdc16c34Maps["Smoke limiter"] = new MapDefinition
            {
                Address = 0x2A740,
                Length = 648,
                Columns = 18,
                Rows = 18,
                Description = "Smoke limiter map for Peugeot EDC16C34",
                AlternativeNames = new List<string> { "Limiteur_fumees", "Controle_fumees", "Limiteur_opacite" }
            };
            peugeotEdc16c34Maps["Target boost"] = new MapDefinition
            {
                Address = 0x2A9C0,
                Length = 648,
                Columns = 18,
                Rows = 18,
                Description = "Target boost map for Peugeot EDC16C34",
                AlternativeNames = new List<string> { "Consigne_suralimentation", "Pression_suralim", "Turbo_consigne" }
            };
            peugeotEdc16c34Maps["Injection quantity"] = new MapDefinition
            {
                Address = 0x2AC40,
                Length = 648,
                Columns = 18,
                Rows = 18,
                Description = "Injection quantity map for Peugeot EDC16C34",
                AlternativeNames = new List<string> { "Quantite_injection", "Debit_injecteur", "Quantite_carburant" }
            };
            peugeotEdc16c34Maps["Start of injection"] = new MapDefinition
            {
                Address = 0x2AEC0,
                Length = 648,
                Columns = 18,
                Rows = 18,
                Description = "Start of injection map for Peugeot EDC16C34",
                AlternativeNames = new List<string> { "Debut_injection", "Avance_injection", "SOI" }
            };
            peugeotEdc16c34Maps["Rail pressure"] = new MapDefinition
            {
                Address = 0x2B140,
                Length = 648,
                Columns = 18,
                Rows = 18,
                Description = "Rail pressure map for Peugeot EDC16C34",
                AlternativeNames = new List<string> { "Pression_rampe", "Common_rail", "Pression_rail_carburant" }
            };
            peugeotEdc16c34Maps["EGR position"] = new MapDefinition
            {
                Address = 0x2B3C0,
                Length = 648,
                Columns = 18,
                Rows = 18,
                Description = "EGR position map for Peugeot EDC16C34",
                AlternativeNames = new List<string> { "Position_EGR", "EGR_ouverture", "Vanne_EGR" }
            };
            peugeotEdc16c34Maps["Turbo position"] = new MapDefinition
            {
                Address = 0x2B640,
                Length = 648,
                Columns = 18,
                Rows = 18,
                Description = "Turbo position map for Peugeot EDC16C34",
                AlternativeNames = new List<string> { "Position_turbo", "Geometrie_variable", "VGT_position" }
            };
            peugeotEdc16c34Maps["DPF regeneration"] = new MapDefinition
            {
                Address = 0x2B8C0,
                Length = 324,
                Columns = 18,
                Rows = 9,
                Description = "DPF regeneration map for Peugeot EDC16C34",
                AlternativeNames = new List<string> { "Regeneration_FAP", "FAP_regen", "DPF_regen" }
            };

            defaultDefs["PEUGEOT_EDC16C34"] = peugeotEdc16c34Maps;

            // Ajout des définitions PEUGEOT EDC17C60 (moteurs e-HDI)
            var peugeotEdc17Maps = new Dictionary<string, MapDefinition>();
            peugeotEdc17Maps["Driver wish"] = new MapDefinition
            {
                Address = 0x4A240,
                Length = 800,
                Columns = 20,
                Rows = 20,
                Description = "Driver wish map for Peugeot EDC17C60",
                AlternativeNames = new List<string> { "Demande_conducteur", "Pedale_accelerateur", "Consigne_conducteur" }
            };
            peugeotEdc17Maps["Torque limiter"] = new MapDefinition
            {
                Address = 0x4A540,
                Length = 800,
                Columns = 20,
                Rows = 20,
                Description = "Torque limiter map for Peugeot EDC17C60",
                AlternativeNames = new List<string> { "Limiteur_couple", "Couple_maximum", "Limitation_couple" }
            };
            peugeotEdc17Maps["Smoke limiter"] = new MapDefinition
            {
                Address = 0x4A840,
                Length = 800,
                Columns = 20,
                Rows = 20,
                Description = "Smoke limiter map for Peugeot EDC17C60",
                AlternativeNames = new List<string> { "Limiteur_fumees", "Controle_fumees", "Limiteur_opacite" }
            };
            peugeotEdc17Maps["Target boost"] = new MapDefinition
            {
                Address = 0x4AB40,
                Length = 800,
                Columns = 20,
                Rows = 20,
                Description = "Target boost map for Peugeot EDC17C60",
                AlternativeNames = new List<string> { "Consigne_suralimentation", "Pression_suralim", "Turbo_consigne" }
            };
            peugeotEdc17Maps["Injection quantity"] = new MapDefinition
            {
                Address = 0x4AE40,
                Length = 800,
                Columns = 20,
                Rows = 20,
                Description = "Injection quantity map for Peugeot EDC17C60",
                AlternativeNames = new List<string> { "Quantite_injection", "Debit_injecteur", "Quantite_carburant" }
            };
            peugeotEdc17Maps["Start of injection"] = new MapDefinition
            {
                Address = 0x4B140,
                Length = 800,
                Columns = 20,
                Rows = 20,
                Description = "Start of injection map for Peugeot EDC17C60",
                AlternativeNames = new List<string> { "Debut_injection", "Avance_injection", "SOI" }
            };
            peugeotEdc17Maps["Rail pressure"] = new MapDefinition
            {
                Address = 0x4B440,
                Length = 800,
                Columns = 20,
                Rows = 20,
                Description = "Rail pressure map for Peugeot EDC17C60",
                AlternativeNames = new List<string> { "Pression_rampe", "Common_rail", "Pression_rail_carburant" }
            };
            peugeotEdc17Maps["EGR position"] = new MapDefinition
            {
                Address = 0x4B740,
                Length = 800,
                Columns = 20,
                Rows = 20,
                Description = "EGR position map for Peugeot EDC17C60",
                AlternativeNames = new List<string> { "Position_EGR", "EGR_ouverture", "Vanne_EGR" }
            };
            peugeotEdc17Maps["Turbo position"] = new MapDefinition
            {
                Address = 0x4BA40,
                Length = 800,
                Columns = 20,
                Rows = 20,
                Description = "Turbo position map for Peugeot EDC17C60",
                AlternativeNames = new List<string> { "Position_turbo", "Geometrie_variable", "VGT_position" }
            };
            peugeotEdc17Maps["Air mass"] = new MapDefinition
            {
                Address = 0x4BD40,
                Length = 800,
                Columns = 20,
                Rows = 20,
                Description = "Air mass map for Peugeot EDC17C60",
                AlternativeNames = new List<string> { "Masse_air", "Debit_air", "Mass_air_flow" }
            };
            peugeotEdc17Maps["Fuel temperature"] = new MapDefinition
            {
                Address = 0x4C040,
                Length = 512,
                Columns = 16,
                Rows = 16,
                Description = "Fuel temperature map for Peugeot EDC17C60",
                AlternativeNames = new List<string> { "Temperature_carburant", "Temp_gasoil", "Temperature_fuel" }
            };
            peugeotEdc17Maps["DPF regeneration"] = new MapDefinition
            {
                Address = 0x4C340,
                Length = 400,
                Columns = 20,
                Rows = 10,
                Description = "DPF regeneration map for Peugeot EDC17C60",
                AlternativeNames = new List<string> { "Regeneration_FAP", "FAP_regen", "DPF_regen" }
            };

            defaultDefs["PEUGEOT_EDC17C60"] = peugeotEdc17Maps;

            // Ajout des définitions PEUGEOT EDC15C7 (anciens moteurs 1.9 JTD)
            var peugeotEdc15c7Maps = new Dictionary<string, MapDefinition>();
            peugeotEdc15c7Maps["Driver wish"] = new MapDefinition
            {
                Address = 0x1A140,
                Length = 480,
                Columns = 15,
                Rows = 16,
                Description = "Driver wish map for Peugeot EDC15C7",
                AlternativeNames = new List<string> { "Demande_conducteur", "Pedale_accelerateur", "Consigne_conducteur" }
            };
            peugeotEdc15c7Maps["Torque limiter"] = new MapDefinition
            {
                Address = 0x1A340,
                Length = 480,
                Columns = 15,
                Rows = 16,
                Description = "Torque limiter map for Peugeot EDC15C7",
                AlternativeNames = new List<string> { "Limiteur_couple", "Couple_maximum", "Limitation_couple" }
            };
            peugeotEdc15c7Maps["Smoke limiter"] = new MapDefinition
            {
                Address = 0x1A540,
                Length = 480,
                Columns = 15,
                Rows = 16,
                Description = "Smoke limiter map for Peugeot EDC15C7",
                AlternativeNames = new List<string> { "Limiteur_fumees", "Controle_fumees", "Limiteur_opacite" }
            };
            peugeotEdc15c7Maps["Target boost"] = new MapDefinition
            {
                Address = 0x1A740,
                Length = 480,
                Columns = 15,
                Rows = 16,
                Description = "Target boost map for Peugeot EDC15C7",
                AlternativeNames = new List<string> { "Consigne_suralimentation", "Pression_suralim", "Turbo_consigne" }
            };
            peugeotEdc15c7Maps["Injection quantity"] = new MapDefinition
            {
                Address = 0x1A940,
                Length = 480,
                Columns = 15,
                Rows = 16,
                Description = "Injection quantity map for Peugeot EDC15C7",
                AlternativeNames = new List<string> { "Quantite_injection", "Debit_injecteur", "Quantite_carburant" }
            };
            peugeotEdc15c7Maps["Start of injection"] = new MapDefinition
            {
                Address = 0x1AB40,
                Length = 480,
                Columns = 15,
                Rows = 16,
                Description = "Start of injection map for Peugeot EDC15C7",
                AlternativeNames = new List<string> { "Debut_injection", "Avance_injection", "SOI" }
            };
            peugeotEdc15c7Maps["Rail pressure"] = new MapDefinition
            {
                Address = 0x1AD40,
                Length = 480,
                Columns = 15,
                Rows = 16,
                Description = "Rail pressure map for Peugeot EDC15C7",
                AlternativeNames = new List<string> { "Pression_rampe", "Common_rail", "Pression_rail_carburant" }
            };

            defaultDefs["PEUGEOT_EDC15C7"] = peugeotEdc15c7Maps;


        // Ajout des définitions PSA génériques
        defaultDefs["PSA_EDC15C2"] = citroenEdc15Maps;
            defaultDefs["PSA_EDC16C34"] = citroenEdc16Maps;
            defaultDefs["PSA_EDC17C60"] = citroenEdc17Maps;
            // Sauvegarder le fichier
            string jsonContent = JsonConvert.SerializeObject(defaultDefs, Newtonsoft.Json.Formatting.Indented);

            File.WriteAllText(filePath, jsonContent);
        }

        // Obtient l'adresse d'une carte spécifique pour l'ECU actuel
        private int GetMapAddress(string mapName)
        {
            // Vérifier si nous avons des définitions pour ce type d'ECU
            if (_mapDefinitions.ContainsKey(_currentECUType))
            {
                var ecuMaps = _mapDefinitions[_currentECUType];

                // Vérifier si cette carte existe pour cet ECU
                if (ecuMaps.ContainsKey(mapName))
                {
                    return ecuMaps[mapName].Address;
                }

                // Vérifier les noms alternatifs
                foreach (var map in ecuMaps)
                {
                    if (map.Value.AlternativeNames.Contains(mapName))
                    {
                        return map.Value.Address;
                    }
                }
            }

            // Si aucune adresse n'est trouvée, retourner 0
            return 0;
        }

        // Nouvelle méthode pour déterminer le type d'ECU
        private string DetermineECUType(byte[] fileBytes, ECUInfo basicInfo)
        {
            string ecuType = basicInfo.EcuType;

            // Si c'est un BMW
            if (basicInfo.CarMake.Contains("BMW"))
            {
                if (SearchForSignature(fileBytes, "BOSCH DDE4.0"))
                    return "BMW_DDE4";
                if (SearchForSignature(fileBytes, "EDC16 BMW") || SearchForSignature(fileBytes, "MSD8"))
                    return "BMW_DDE5";
                if (SearchForSignature(fileBytes, "EDC17 BMW"))
                    return "BMW_DDE6";

                // Estimation par taille
                if (fileBytes.Length <= 524288) // 512Ko
                    return "BMW_DDE3_4";
                else if (fileBytes.Length <= 1048576) // 1Mo
                    return "BMW_DDE4_5";
                else
                    return "BMW_DDE5_6_7";
            }

            // Si c'est un Audi mais pas détecté comme EDC15P
            if (basicInfo.CarMake.Contains("AUDI") && !ecuType.StartsWith("EDC15P"))
            {
                // Détection spécifique pour Audi
                if (SearchForSignature(fileBytes, "EDC16"))
                    return "AUDI_EDC16";
                if (SearchForSignature(fileBytes, "EDC17"))
                    return "AUDI_EDC17";
                if (SearchForSignature(fileBytes, "MED"))
                    return "AUDI_MED";
            }

            // Si c'est un Citroën
            if (basicInfo.CarMake.Contains("Citroën") || basicInfo.CarMake.Contains("CITROËN"))
            {
                // Détection des signatures spécifiques Citroën
                if (SearchForSignature(fileBytes, "BOSCH EDC17C60"))
                    return "CITROEN_EDC17C60";
                if (SearchForSignature(fileBytes, "EDC16C34"))
                    return "CITROEN_EDC16C34";
                if (SearchForSignature(fileBytes, "EDC16C39"))
                    return "CITROEN_EDC16C39";
                if (SearchForSignature(fileBytes, "EDC15C2"))
                    return "CITROEN_EDC15C2";

                // Détection par signature PSA (Peugeot-Citroën)
                if (SearchForSignature(fileBytes, "PSA") || SearchForSignature(fileBytes, "CITROEN"))
                {
                    // Estimation par taille du fichier
                    if (fileBytes.Length <= 524288) // 512Ko - généralement EDC15C2
                        return "CITROEN_EDC15C2";
                    else if (fileBytes.Length <= 1048576) // 1Mo - généralement EDC16C34
                        return "CITROEN_EDC16C34";
                    else if (fileBytes.Length <= 2097152) // 2Mo - généralement EDC17C60
                        return "CITROEN_EDC17C60";
                }

                // Détection par numéros de série PSA (commencent par 96xx)
                if (basicInfo.SoftwareID.StartsWith("96") || basicInfo.SoftwareID.StartsWith("98"))
                {
                    if (fileBytes.Length <= 524288)
                        return "CITROEN_EDC15C2";
                    else if (fileBytes.Length <= 1048576)
                        return "CITROEN_EDC16C34";
                    else
                        return "CITROEN_EDC17C60";
                }
            }

            // Si c'est un calculateur PSA générique (partagé Peugeot/Citroën)
            if (basicInfo.CarMake.Contains("PSA") || basicInfo.SoftwareID.StartsWith("96") || basicInfo.SoftwareID.StartsWith("98"))
            {
                if (SearchForSignature(fileBytes, "EDC17"))
                    return "PSA_EDC17C60";
                if (SearchForSignature(fileBytes, "EDC16"))
                    return "PSA_EDC16C34";
                if (SearchForSignature(fileBytes, "EDC15"))
                    return "PSA_EDC15C2";

                // Estimation par taille
                if (fileBytes.Length <= 524288)
                    return "PSA_EDC15C2";
                else if (fileBytes.Length <= 1048576)
                    return "PSA_EDC16C34";
                else
                    return "PSA_EDC17C60";
            }

            // Si c'est un Peugeot
            if (basicInfo.CarMake.Contains("Peugeot") || basicInfo.CarMake.Contains("PEUGEOT"))
            {
                // Détection des signatures spécifiques Peugeot
                if (SearchForSignature(fileBytes, "BOSCH EDC17C60"))
                    return "PEUGEOT_EDC17C60";
                if (SearchForSignature(fileBytes, "EDC16C34"))
                    return "PEUGEOT_EDC16C34";
                if (SearchForSignature(fileBytes, "EDC16C3"))
                    return "PEUGEOT_EDC16C3";
                if (SearchForSignature(fileBytes, "EDC16C39"))
                    return "PEUGEOT_EDC16C39";
                if (SearchForSignature(fileBytes, "EDC15C2"))
                    return "PEUGEOT_EDC15C2";
                if (SearchForSignature(fileBytes, "EDC15C7"))
                    return "PEUGEOT_EDC15C7";

                // Détection par signature PSA (Peugeot-Citroën)
                if (SearchForSignature(fileBytes, "PSA") || SearchForSignature(fileBytes, "PEUGEOT"))
                {
                    // Estimation par taille du fichier
                    if (fileBytes.Length <= 524288) // 512Ko - généralement EDC15C2
                        return "PEUGEOT_EDC15C2";
                    else if (fileBytes.Length <= 1048576) // 1Mo - généralement EDC16C34/C3
                        return "PEUGEOT_EDC16C34";
                    else if (fileBytes.Length <= 2097152) // 2Mo - généralement EDC17C60
                        return "PEUGEOT_EDC17C60";
                }

                // Détection par numéros de série PSA (commencent par 96xx, 98xx)
                if (basicInfo.SoftwareID.StartsWith("96") || basicInfo.SoftwareID.StartsWith("98") ||
                    basicInfo.SoftwareID.StartsWith("103"))
                {
                    if (fileBytes.Length <= 524288)
                        return "PEUGEOT_EDC15C2";
                    else if (fileBytes.Length <= 1048576)
                    {
                        // Différencier EDC16C3 et EDC16C34 par la taille et contenu
                        if (SearchForSignature(fileBytes, "EDC16C3") || fileBytes.Length <= 786432) // 768Ko
                            return "PEUGEOT_EDC16C3";
                        else
                            return "PEUGEOT_EDC16C34";
                    }
                    else
                        return "PEUGEOT_EDC17C60";
                }

                // Détection par type de moteur
                if (basicInfo.EngineType.Contains("1.4") && basicInfo.EngineType.Contains("HDI"))
                {
                    if (fileBytes.Length <= 786432) // 768Ko
                        return "PEUGEOT_EDC16C3";
                    else
                        return "PEUGEOT_EDC16C34";
                }
                else if (basicInfo.EngineType.Contains("1.6") && basicInfo.EngineType.Contains("HDI"))
                {
                    if (basicInfo.EngineType.Contains("e-HDI"))
                        return "PEUGEOT_EDC17C60";
                    else
                        return "PEUGEOT_EDC16C34";
                }
                else if (basicInfo.EngineType.Contains("2.0") && basicInfo.EngineType.Contains("HDI"))
                {
                    return "PEUGEOT_EDC15C2";
                }
            }


            // Si c'est un Peugeot
            if (basicInfo.CarMake.Contains("Peugeot") || basicInfo.CarMake.Contains("PEUGEOT"))
            {
                // Détection des signatures spécifiques Peugeot
                if (SearchForSignature(fileBytes, "BOSCH EDC17C60"))
                    return "PEUGEOT_EDC17C60";
                if (SearchForSignature(fileBytes, "EDC16C34"))
                    return "PEUGEOT_EDC16C34";
                if (SearchForSignature(fileBytes, "EDC16C3"))
                    return "PEUGEOT_EDC16C3";
                if (SearchForSignature(fileBytes, "EDC16C39"))
                    return "PEUGEOT_EDC16C39";
                if (SearchForSignature(fileBytes, "EDC15C2"))
                    return "PEUGEOT_EDC15C2";
                if (SearchForSignature(fileBytes, "EDC15C7"))
                    return "PEUGEOT_EDC15C7";

                // Détection par signature PSA (Peugeot-Citroën)
                if (SearchForSignature(fileBytes, "PSA") || SearchForSignature(fileBytes, "PEUGEOT"))
                {
                    // Estimation par taille du fichier
                    if (fileBytes.Length <= 524288) // 512Ko - généralement EDC15C2
                        return "PEUGEOT_EDC15C2";
                    else if (fileBytes.Length <= 1048576) // 1Mo - généralement EDC16C34/C3
                        return "PEUGEOT_EDC16C34";
                    else if (fileBytes.Length <= 2097152) // 2Mo - généralement EDC17C60
                        return "PEUGEOT_EDC17C60";
                }

                // Détection par numéros de série PSA (commencent par 96xx, 98xx)
                if (basicInfo.SoftwareID.StartsWith("96") || basicInfo.SoftwareID.StartsWith("98") ||
                    basicInfo.SoftwareID.StartsWith("103"))
                {
                    if (fileBytes.Length <= 524288)
                        return "PEUGEOT_EDC15C2";
                    else if (fileBytes.Length <= 1048576)
                    {
                        // Différencier EDC16C3 et EDC16C34 par la taille et contenu
                        if (SearchForSignature(fileBytes, "EDC16C3") || fileBytes.Length <= 786432) // 768Ko
                            return "PEUGEOT_EDC16C3";
                        else
                            return "PEUGEOT_EDC16C34";
                    }
                    else
                        return "PEUGEOT_EDC17C60";
                }

                // Détection par type de moteur
                if (basicInfo.EngineType.Contains("1.4") && basicInfo.EngineType.Contains("HDI"))
                {
                    if (fileBytes.Length <= 786432) // 768Ko
                        return "PEUGEOT_EDC16C3";
                    else
                        return "PEUGEOT_EDC16C34";
                }
                else if (basicInfo.EngineType.Contains("1.6") && basicInfo.EngineType.Contains("HDI"))
                {
                    if (basicInfo.EngineType.Contains("e-HDI"))
                        return "PEUGEOT_EDC17C60";
                    else
                        return "PEUGEOT_EDC16C34";
                }
                else if (basicInfo.EngineType.Contains("2.0") && basicInfo.EngineType.Contains("HDI"))
                {
                    return "PEUGEOT_EDC15C2";
                }
            }
            // Retourne le type par défaut si aucun match spécifique
            return ecuType;
        }



        // Méthode pour trouver un symbole 3D aléatoire
        private SymbolHelper GetRandom3DSymbol(SymbolCollection symbols)
        {
            List<SymbolHelper> symbols3D = new List<SymbolHelper>();

            // Récupérer tous les symboles 3D
            foreach (SymbolHelper sh in symbols)
            {
                if (sh.Varname.StartsWith("3D") && sh.Flash_start_address > 0)
                {
                    symbols3D.Add(sh);
                }
            }

            // Si aucun symbole 3D n'est trouvé, retourner null
            if (symbols3D.Count == 0)
                return null;

            // Générer un index aléatoire et retourner le symbole correspondant
            Random random = new Random();
            int randomIndex = random.Next(0, symbols3D.Count);
            return symbols3D[randomIndex];
        }

        // Méthode pour rechercher une signature dans le fichier
        private bool SearchForSignature(byte[] fileBytes, string signature)
        {
            if (fileBytes == null || string.IsNullOrEmpty(signature))
                return false;

            byte[] signatureBytes = Encoding.ASCII.GetBytes(signature);

            // Recherche dans tout le fichier
            for (int i = 0; i <= fileBytes.Length - signatureBytes.Length; i++)
            {
                bool found = true;
                for (int j = 0; j < signatureBytes.Length; j++)
                {
                    if (fileBytes[i + j] != signatureBytes[j])
                    {
                        found = false;
                        break;
                    }
                }

                if (found)
                    return true;
            }

            return false;
        }


        /// <summary>
        /// Recherche une signature avec des variations de casse
        /// </summary>
        /// <param name="fileBytes">Tableau d'octets du fichier</param>
        /// <param name="signature">Signature à rechercher</param>
        /// <returns>True si la signature est trouvée, false sinon</returns>
        private bool SearchForSignatureIgnoreCase(byte[] fileBytes, string signature)
        {
            // Recherche en majuscules
            if (SearchForSignature(fileBytes, signature.ToUpper()))
                return true;

            // Recherche en minuscules
            if (SearchForSignature(fileBytes, signature.ToLower()))
                return true;

            // Recherche tel quel
            return SearchForSignature(fileBytes, signature);
        }



        private int GetMapCount(string varName, SymbolCollection newSymbols)
        {
            int mapCount = 0;
            foreach (SymbolHelper sh in newSymbols)
            {
                if (sh.Varname.StartsWith(varName)) mapCount ++;
            }
            return mapCount;
        }

        //private bool MapsWithNameMissing(string varName, SymbolCollection newSymbols)
        //{
        //    foreach (SymbolHelper sh in newSymbols)
        //    {
        //        if(sh.Varname.StartsWith(varName)) return false;
        //    }
        //    return true;
        //}

        // Fonction modifiée pour rechercher les cartes avec différentes variantes de noms (pour Audi)
        //private bool MapsWithNameMissing(string mapName, SymbolCollection symbols)
        //{
        //    // Créer une liste de variantes possibles pour les noms de cartes Audi
        //    List<string> possibleNames = new List<string>
        //    {
        //        mapName,
        //        mapName.Replace(" ", "_"),
        //        mapName.Replace(" ", ""),
        //        "Audi " + mapName,
        //        // Ajoutez des alternatives spécifiques selon le type de carte
        //        mapName == "Torque limiter" ? "Engine_torque_limit" : "",
        //        mapName == "Torque limiter" ? "Maximum_torque" : "",
        //        mapName == "Smoke limiter" ? "Smoke_control" : "",
        //    };

        //    // Filtrer les chaînes vides
        //    possibleNames = possibleNames.Where(n => !string.IsNullOrEmpty(n)).ToList();

        //    // Vérifier si l'une des variantes est présente
        //    foreach (string name in possibleNames)
        //    {
        //        foreach (SymbolHelper sh in symbols)
        //        {
        //            // Recherche plus flexible
        //            if (sh.Varname.Contains(name) || sh.Userdescription.Contains(name))
        //            {
        //                return false; // Trouvé, donc pas manquant
        //            }
        //        }
        //    }
        //    return true; // Aucune variante trouvée, donc manquant
        //}

        private bool MapsWithNameMissing(string mapName, SymbolCollection symbols)
        {
            // Créer différentes variations du nom
            List<string> nameVariations = new List<string>
            {
                mapName,
                mapName.Replace(" ", "_"),
                mapName.Replace(" ", ""),
                mapName ,
                mapName.ToLower(),
                mapName.ToUpper()
            };

            // Vérifier chaque symbole pour toutes les variations de nom
            foreach (SymbolHelper sh in symbols)
            {
                foreach (string variation in nameVariations)
                {
                    // Recherche plus flexible dans le nom et la description
                    if (sh.Varname.IndexOf(variation, StringComparison.OrdinalIgnoreCase) >= 0 ||
                        sh.Userdescription.IndexOf(variation, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return false; // Carte trouvée
                    }
                }
            }

            return true; // Carte non trouvée
        }
        private List<string> GetAlternativeMapNames(string mapName, string ecuType, string carMake)
        {
            List<string> names = new List<string>();

            // Noms de base (pour tous les ECU)
            names.Add(mapName.Replace(" ", "_"));
            names.Add(mapName.Replace(" ", ""));

            // Ajout des noms spécifiques par type d'ECU
            if (ecuType.Contains("BMW") || carMake.Contains("BMW"))
            {
                switch (mapName)
                {
                    case "Driver wish":
                        names.AddRange(new[] { "FahrPedal_Kennfeld", "Pedal_Map", "Gaspedalkennlinie" });
                        break;
                    case "Torque limiter":
                        names.AddRange(new[] { "Drehmoment_Begrenzer", "MaxDrehmoment", "Drehmomentbegrenzung" });
                        break;
                    case "Smoke limiter":
                        names.AddRange(new[] { "Rauchbegrenzung", "Smoke_Control", "RauchKennfeld" });
                        break;
                    case "Target boost":
                        names.AddRange(new[] { "Ladedruck_Sollwert", "Boost_Target", "SollLadedruck" });
                        break;
                    case "Boost pressure limiter":
                        names.AddRange(new[] { "Ladedruck_Begrenzung", "MaxLadedruck" });
                        break;
                    case "EGR":
                        names.AddRange(new[] { "AGR_Kennfeld", "Exhaust_Gas_Recirculation", "AGR_Rate" });
                        break;
                }
            }

            else if (ecuType.Contains("AUDI") || carMake.Contains("AUDI"))
            {
                switch (mapName)
                {
                    case "Driver wish":
                        names.AddRange(new[] { "Fahrerwunsch", "Audi_Driver_Wish", "Kennfeld_Fahrpedal" });
                        break;
                    case "Torque limiter":
                        names.AddRange(new[] { "Audi_Torque_Limiter", "Maximum_Torque", "Drehmoment_Max" });
                        break;
                    case "Smoke limiter":
                        names.AddRange(new[] { "Audi_Smoke_Limiter", "Smoke_Control", "Rauch_Begrenzung" });
                        break;
                    case "Target boost":
                        names.AddRange(new[] { "Audi_Target_Boost", "Soll_Ladedruck", "Requested_Boost" });
                        break;
                    case "Boost pressure limiter":
                        names.AddRange(new[] { "Audi_Boost_Limiter", "Max_Boost", "Ladedruck_Max" });
                        break;
                    case "EGR":
                        names.AddRange(new[] { "Audi_EGR", "Abgasrückführung", "AGR_Kennfeld" });
                        break;
                }
            }
            // Ajout des noms spécifiques par type d'ECU pour Citroën
            if (ecuType.Contains("CITROEN") || carMake.Contains("Citroën") || ecuType.Contains("PSA"))
            {
                switch (mapName)
                {
                    case "Driver wish":
                        names.AddRange(new[] {
                            "Consigne_conducteur", "Demande_conducteur", "Pedale_accelerateur",
                            "Accelerator_pedal", "Fahrer_wunsch", "Conducteur_souhait"
                        });
                        break;
                    case "Torque limiter":
                        names.AddRange(new[] {
                            "Limiteur_couple", "Couple_maximum", "Limitation_couple",
                            "Torque_limit", "Drehmoment_begrenzer", "Max_couple"
                        });
                        break;
                    case "Smoke limiter":
                        names.AddRange(new[] {
                            "Limiteur_fumees", "Controle_fumees", "Smoke_limit",
                            "Limiteur_opacite", "Opacite_maximale", "Fumee_limite"
                        });
                        break;
                    case "Target boost":
                        names.AddRange(new[] {
                            "Consigne_suralimentation", "Pression_suralim", "Boost_target",
                            "Turbo_consigne", "Suralimentation_cible", "Pression_admission"
                        });
                        break;
                    case "Boost pressure limiter":
                        names.AddRange(new[] {
                            "Limiteur_suralimentation", "Pression_max_turbo", "Max_boost",
                            "Limiteur_pression_admission", "Suralimentation_limite"
                        });
                        break;
                    case "Injection quantity":
                        names.AddRange(new[] {
                            "Quantite_injection", "Debit_injecteur", "Injection_qty",
                            "Quantite_carburant", "Debit_carburant", "Qty_injection"
                        });
                        break;
                    case "Start of injection":
                        names.AddRange(new[] {
                            "Debut_injection", "Avance_injection", "SOI",
                            "Timing_injection", "Calage_injection", "Start_injection"
                        });
                        break;
                    case "Injection pressure":
                        names.AddRange(new[] {
                            "Pression_injection", "Pression_rail", "Rail_pressure",
                            "Common_rail_pressure", "Pression_rampe", "Injection_press"
                        });
                        break;
                    case "EGR position":
                        names.AddRange(new[] {
                            "Position_EGR", "EGR_ouverture", "Vanne_EGR",
                            "EGR_valve", "Recirculation_gaz", "EGR_pos"
                        });
                        break;
                    case "Turbo position":
                        names.AddRange(new[] {
                            "Position_turbo", "Geometrie_variable", "VGT_position",
                            "Turbo_geometry", "Variable_geometry", "VNT_position"
                        });
                        break;
                    case "Rail pressure":
                        names.AddRange(new[] {
                            "Pression_rampe", "Common_rail", "Rail_press",
                            "Pression_rail_carburant", "Rampe_pression"
                        });
                        break;
                    case "Fuel temperature":
                        names.AddRange(new[] {
                            "Temperature_carburant", "Temp_gasoil", "Fuel_temp",
                            "Temperature_fuel", "Temp_carburant"
                        });
                        break;
                    case "Air mass":
                        names.AddRange(new[] {
                            "Masse_air", "Debit_air", "Air_flow",
                            "Mass_air_flow", "MAF", "Debit_masse_air"
                        });
                        break;
                }
            }

            // Ajout des noms spécifiques par type d'ECU pour Peugeot
            if (ecuType.Contains("PEUGEOT") || carMake.Contains("Peugeot"))
            {
                switch (mapName)
                {
                    case "Driver wish":
                        names.AddRange(new[] {
                            "Demande_conducteur", "Pedale_accelerateur", "Consigne_conducteur",
                            "Accelerator_pedal", "Driver_request", "Conducteur_demande",
                            "Peugeot_Driver_Wish", "Fahrer_wunsch"
                        });
                        break;
                    case "Torque limiter":
                        names.AddRange(new[] {
                            "Limiteur_couple", "Couple_maximum", "Limitation_couple",
                            "Torque_limit", "Max_couple", "Peugeot_Torque_Limiter",
                            "Drehmoment_begrenzer", "Couple_max"
                        });
                        break;
                    case "Smoke limiter":
                        names.AddRange(new[] {
                            "Limiteur_fumees", "Controle_fumees", "Limiteur_opacite",
                            "Smoke_limit", "Opacite_maximale", "Fumee_limite",
                            "Peugeot_Smoke_Limiter", "Smoke_control"
                        });
                        break;
                    case "Target boost":
                        names.AddRange(new[] {
                            "Consigne_suralimentation", "Pression_suralim", "Turbo_consigne",
                            "Boost_target", "Suralimentation_cible", "Pression_admission",
                            "Peugeot_Target_Boost", "Soll_ladedruck"
                        });
                        break;
                    case "Boost pressure limiter":
                        names.AddRange(new[] {
                            "Limiteur_suralimentation", "Pression_max_turbo", "Max_boost",
                            "Limiteur_pression_admission", "Suralimentation_limite",
                            "Peugeot_Boost_Limiter", "Ladedruck_max"
                        });
                        break;
                    case "Injection quantity":
                        names.AddRange(new[] {
                            "Quantite_injection", "Debit_injecteur", "Quantite_carburant",
                            "Injection_qty", "Debit_carburant", "Qty_injection",
                            "Peugeot_IQ", "Einspritzmenge"
                        });
                        break;
                    case "Start of injection":
                        names.AddRange(new[] {
                            "Debut_injection", "Avance_injection", "Timing_injection",
                            "SOI", "Calage_injection", "Start_injection",
                            "Peugeot_SOI", "Einspritzbeginn"
                        });
                        break;
                    case "Injection pressure":
                        names.AddRange(new[] {
                            "Pression_injection", "Pression_rail", "Common_rail_pressure",
                            "Rail_pressure", "Pression_rampe", "Injection_press",
                            "Peugeot_Rail_Pressure", "Kraftstoffdruck"
                        });
                        break;
                    case "EGR position":
                        names.AddRange(new[] {
                            "Position_EGR", "EGR_ouverture", "Vanne_EGR",
                            "EGR_valve", "Recirculation_gaz", "EGR_pos",
                            "Peugeot_EGR", "AGR_ventil"
                        });
                        break;
                    case "Turbo position":
                        names.AddRange(new[] {
                            "Position_turbo", "Geometrie_variable", "VGT_position",
                            "Turbo_geometry", "Variable_geometry", "VNT_position",
                            "Peugeot_VGT", "Turbolader_position"
                        });
                        break;
                    case "Rail pressure":
                        names.AddRange(new[] {
                            "Pression_rampe", "Common_rail", "Pression_rail_carburant",
                            "Rail_press", "Rampe_pression", "Peugeot_Rail",
                            "Rail_kraftstoffdruck"
                        });
                        break;
                    case "Fuel temperature":
                        names.AddRange(new[] {
                            "Temperature_carburant", "Temp_gasoil", "Temperature_fuel",
                            "Fuel_temp", "Temp_carburant", "Peugeot_Fuel_Temp",
                            "Kraftstofftemperatur"
                        });
                        break;
                    case "Air mass":
                        names.AddRange(new[] {
                            "Masse_air", "Debit_air", "Mass_air_flow",
                            "Air_flow", "MAF", "Debit_masse_air",
                            "Peugeot_MAF", "Luftmassenstrom"
                        });
                        break;
                    case "DPF regeneration":
                        names.AddRange(new[] {
                            "Regeneration_FAP", "FAP_regen", "DPF_regen",
                            "Filtre_particules", "Particulate_filter", "Peugeot_DPF"
                        });
                        break;
                }
            }
            return names;
        }





        private void gridView1_DoubleClick(object sender, EventArgs e)
        {
            //TODO: only if mouse on datarow?
            object o = gridViewSymbols.GetFocusedRow();
            if (o is SymbolHelper)
            {
                //SymbolHelper sh = (SymbolHelper)o;
                StartTableViewer();
            }
        }

        private void gridView1_CustomDrawCell(object sender, DevExpress.XtraGrid.Views.Base.RowCellCustomDrawEventArgs e)
        {
            try
            {
                if (e.Column.Name == gcSymbolAddress.Name)
                {
                    if (e.CellValue != null)
                    {
                        //e.DisplayText = Convert.ToInt32(e.CellValue).ToString("X8");
                    }
                }
                else if (e.Column.Name == gcSymbolXID.Name || e.Column.Name == gcSymbolYID.Name)
                {
                }
                else if (e.Column.Name == gcSymbolLength.Name)
                {
                    if (e.CellValue != null)
                    {
                        int len = Convert.ToInt32(e.CellValue);
                        len /= 2;
                        //  e.DisplayText = len.ToString();
                    }
                }
            }
            catch (Exception)
            {

            }
        }

        private void StartTableViewer()
        {
            if (gridViewSymbols.SelectedRowsCount > 0)
            {
                int[] selrows = gridViewSymbols.GetSelectedRows();
                if (selrows.Length > 0)
                {
                    int row = (int)selrows.GetValue(0);
                    if (row >= 0)
                    {
                        SymbolHelper sh = (SymbolHelper)gridViewSymbols.GetRow((int)selrows.GetValue(0));
                        if (sh.Flash_start_address == 0 && sh.Start_address == 0) return;
                        
                        if (sh == null) return;
                        DevExpress.XtraBars.Docking.DockPanel dockPanel;
                        bool pnlfound = false;
                        pnlfound = CheckMapViewerActive(sh);
                        if (!pnlfound)
                        {
                            dockManager1.BeginUpdate();
                            try
                            {
                                MapViewerEx tabdet = new MapViewerEx();
                                tabdet.AutoUpdateIfSRAM = false;
                                tabdet.AutoUpdateInterval = 99999;
                                tabdet.SetViewSize(ViewSize.NormalView);
                                tabdet.Visible = false;
                                tabdet.Filename = Tools.Instance.m_currentfile;
                                tabdet.GraphVisible = true;
                                tabdet.Viewtype = m_appSettings.DefaultViewType;
                                tabdet.DisableColors = m_appSettings.DisableMapviewerColors;
                                tabdet.AutoSizeColumns = m_appSettings.AutoSizeColumnsInWindows;
                                tabdet.GraphVisible = m_appSettings.ShowGraphs;
                                tabdet.IsRedWhite = m_appSettings.ShowRedWhite;
                                tabdet.SetViewSize(m_appSettings.DefaultViewSize);
                                tabdet.Map_name = sh.Varname;
                                tabdet.Map_descr = tabdet.Map_name;
                                tabdet.Map_cat = XDFCategories.Undocumented;
                                SymbolAxesTranslator axestrans = new SymbolAxesTranslator();
                                string x_axis = string.Empty;
                                string y_axis = string.Empty;
                                string x_axis_descr = string.Empty;
                                string y_axis_descr = string.Empty;
                                string z_axis_descr = string.Empty;
                                tabdet.X_axis_name = sh.X_axis_descr;
                                tabdet.Y_axis_name = sh.Y_axis_descr;
                                tabdet.Z_axis_name = sh.Z_axis_descr;
                                tabdet.XaxisUnits = sh.XaxisUnits;
                                tabdet.YaxisUnits = sh.YaxisUnits;
                                tabdet.X_axisAddress = sh.Y_axis_address;
                                tabdet.Y_axisAddress = sh.X_axis_address;

                                tabdet.Xaxiscorrectionfactor = sh.X_axis_correction;
                                tabdet.Yaxiscorrectionfactor = sh.Y_axis_correction;
                                tabdet.Xaxiscorrectionoffset = sh.X_axis_offset;
                                tabdet.Yaxiscorrectionoffset = sh.Y_axis_offset;

                                tabdet.X_axisvalues = GetXaxisValues(Tools.Instance.m_currentfile, Tools.Instance.m_symbols, tabdet.Map_name);
                                tabdet.Y_axisvalues = GetYaxisValues(Tools.Instance.m_currentfile, Tools.Instance.m_symbols, tabdet.Map_name);


                                dockPanel = dockManager1.AddPanel(DevExpress.XtraBars.Docking.DockingStyle.Right);
                                int dw = 650;
                                if (tabdet.X_axisvalues.Length > 0)
                                {
                                    dw = 30 + ((tabdet.X_axisvalues.Length + 1) * 45);
                                }
                                if (dw < 400) dw = 400;
                                if (dw > 800) dw = 800;
                                dockPanel.FloatSize = new Size(dw, 900);



                                dockPanel.Tag = Tools.Instance.m_currentfile;
                                dockPanel.ClosedPanel += new DevExpress.XtraBars.Docking.DockPanelEventHandler(dockPanel_ClosedPanel);

                                int columns = 8;
                                int rows = 8;
                                int tablewidth = GetTableMatrixWitdhByName(Tools.Instance.m_currentfile, Tools.Instance.m_symbols, tabdet.Map_name, out columns, out rows);
                                int address = Convert.ToInt32(sh.Flash_start_address);
                                int sramaddress = 0;
                                if (address != 0)
                                {
                                    tabdet.Map_address = address;
                                    tabdet.Map_sramaddress = sramaddress;
                                    int length = Convert.ToInt32(sh.Length);
                                    tabdet.Map_length = length;
                                    byte[] mapdata = new byte[sh.Length];
                                    mapdata.Initialize();
                                    mapdata = Tools.Instance.readdatafromfile(Tools.Instance.m_currentfile, address, length, Tools.Instance.m_currentFileType);
                                    tabdet.Map_content = mapdata;
                                    tabdet.Correction_factor = sh.Correction;// GetMapCorrectionFactor(tabdet.Map_name);
                                    tabdet.Correction_offset = sh.Offset;// GetMapCorrectionOffset(tabdet.Map_name);
                                    tabdet.IsUpsideDown = m_appSettings.ShowTablesUpsideDown;
                                    tabdet.ShowTable(columns, true);
                                    tabdet.Dock = DockStyle.Fill;
                                    tabdet.onSymbolSave += new VAGSuite.MapViewerEx.NotifySaveSymbol(tabdet_onSymbolSave);
                                    tabdet.onSymbolRead += new VAGSuite.MapViewerEx.NotifyReadSymbol(tabdet_onSymbolRead);
                                    tabdet.onClose += new VAGSuite.MapViewerEx.ViewerClose(tabdet_onClose);
                                    tabdet.onAxisEditorRequested += new MapViewerEx.AxisEditorRequested(tabdet_onAxisEditorRequested);

                                    tabdet.onSliderMove +=new MapViewerEx.NotifySliderMove(tabdet_onSliderMove);
                                    tabdet.onSplitterMoved +=new MapViewerEx.SplitterMoved(tabdet_onSplitterMoved);
                                    tabdet.onSelectionChanged +=new MapViewerEx.SelectionChanged(tabdet_onSelectionChanged);
                                    tabdet.onSurfaceGraphViewChangedEx += new MapViewerEx.SurfaceGraphViewChangedEx(tabdet_onSurfaceGraphViewChangedEx);
                                    tabdet.onAxisLock +=new MapViewerEx.NotifyAxisLock(tabdet_onAxisLock);
                                    tabdet.onViewTypeChanged +=new MapViewerEx.ViewTypeChanged(tabdet_onViewTypeChanged);

                                    dockPanel.Text = "Symbol: " + tabdet.Map_name + " [" + Path.GetFileName(Tools.Instance.m_currentfile) + "]";
                                    bool isDocked = false;

                                    if (!isDocked)
                                    {
                                        int width = 500;
                                        if (tabdet.X_axisvalues.Length > 0)
                                        {
                                            width = 30 + ((tabdet.X_axisvalues.Length + 1) * 45);
                                        }
                                        if (width < 500) width = 500;
                                        if (width > 800) width = 800;

                                        dockPanel.Width = width;
                                    }
                                    dockPanel.Controls.Add(tabdet);
                                }
                                else
                                {
                                    byte[] mapdata = new byte[sh.Length];
                                    mapdata.Initialize();

                                }
                                tabdet.Visible = true;
                            }
                            catch (Exception newdockE)
                            {
                                Console.WriteLine(newdockE.Message);
                            }
                            Console.WriteLine("End update");
                            dockManager1.EndUpdate();
                        }
                    }
                    System.Windows.Forms.Application.DoEvents();
                }
            }

        }

        private bool CheckMapViewerActive(SymbolHelper sh)
        {
            bool retval = false;
            try
            {
                foreach (DevExpress.XtraBars.Docking.DockPanel pnl in dockManager1.Panels)
                {
                    if (pnl.Text == "Symbol: " + sh.Varname + " [" + Path.GetFileName(Tools.Instance.m_currentfile) + "]")
                    {
                        if (pnl.Tag.ToString() == Tools.Instance.m_currentfile)
                        {
                            if (isSymbolDisplaySameAddress(sh, pnl))
                            {
                                retval = true;
                                pnl.Show();
                            }
                        }
                    }
                }
            }
            catch (Exception E)
            {
                Console.WriteLine(E.Message);
            }
            return retval;
        }

        private bool isSymbolDisplaySameAddress(SymbolHelper sh, DockPanel pnl)
        {
            bool retval = false;
            try
            {
                if (pnl.Text.StartsWith("Symbol: "))
                {
                    foreach (Control c in pnl.Controls)
                    {
                        if (c is MapViewerEx)
                        {
                            MapViewerEx vwr = (MapViewerEx)c;
                            if (vwr.Map_address == sh.Flash_start_address) retval = true;
                        }
                        else if (c is DevExpress.XtraBars.Docking.DockPanel)
                        {
                            DevExpress.XtraBars.Docking.DockPanel tpnl = (DevExpress.XtraBars.Docking.DockPanel)c;
                            foreach (Control c2 in tpnl.Controls)
                            {
                                if (c2 is MapViewerEx)
                                {
                                    MapViewerEx vwr2 = (MapViewerEx)c2;
                                    if (vwr2.Map_address == sh.Flash_start_address) retval = true;

                                }
                            }
                        }
                        else if (c is DevExpress.XtraBars.Docking.ControlContainer)
                        {
                            DevExpress.XtraBars.Docking.ControlContainer cntr = (DevExpress.XtraBars.Docking.ControlContainer)c;
                            foreach (Control c3 in cntr.Controls)
                            {
                                if (c3 is MapViewerEx)
                                {
                                    MapViewerEx vwr3 = (MapViewerEx)c3;
                                    if (vwr3.Map_address == sh.Flash_start_address) retval = true;
                                }
                            }
                        }
                    }


                }
            }
            catch (Exception E)
            {
                Console.WriteLine("isSymbolDisplaySameAddress error: " + E.Message);
            }
            return retval;
        }

        void tabdet_onAxisEditorRequested(object sender, MapViewerEx.AxisEditorRequestedEventArgs e)
        {
            // start axis editor
            foreach (SymbolHelper sh in Tools.Instance.m_symbols)
            {
                if (sh.Varname == e.Mapname)
                {
                    if (e.Axisident == MapViewerEx.AxisIdent.X_Axis) StartAxisViewer(sh, Axis.XAxis);
                    else if (e.Axisident == MapViewerEx.AxisIdent.Y_Axis) StartAxisViewer(sh, Axis.YAxis);

                    break;
                }
            }
        }

        void tabdet_onClose(object sender, EventArgs e)
        {
            // close the corresponding dockpanel
            if (sender is MapViewerEx)
            {
                MapViewerEx tabdet = (MapViewerEx)sender;
                string dockpanelname = "Symbol: " + tabdet.Map_name + " [" + Path.GetFileName(tabdet.Filename) + "]";
                string dockpanelname3 = "Symbol difference: " + tabdet.Map_name + " [" + Path.GetFileName(tabdet.Filename) + "]";
                foreach (DevExpress.XtraBars.Docking.DockPanel dp in dockManager1.Panels)
                {
                    if (dp.Text == dockpanelname)
                    {
                        dockManager1.RemovePanel(dp);
                        break;
                    }
                    else if (dp.Text == dockpanelname3)
                    {
                        dockManager1.RemovePanel(dp);
                        break;
                    }
                }
            }
        }

        void tabdet_onSymbolRead(object sender, MapViewerEx.ReadSymbolEventArgs e)
        {
            if (sender is MapViewerEx)
            {
                MapViewerEx mv = (MapViewerEx)sender;
                mv.Map_content = Tools.Instance.readdatafromfile(e.Filename, (int)GetSymbolAddress(Tools.Instance.m_symbols, e.SymbolName), GetSymbolLength(Tools.Instance.m_symbols, e.SymbolName), Tools.Instance.m_currentFileType);
                int cols = 0;
                int rows = 0;
                GetTableMatrixWitdhByName(e.Filename, Tools.Instance.m_symbols, e.SymbolName, out cols, out rows);
                mv.IsRAMViewer = false;
                mv.OnlineMode = false;
                mv.ShowTable(cols, true);
                mv.IsRAMViewer = false;
                mv.OnlineMode = false;
                System.Windows.Forms.Application.DoEvents();
            }
        }

        void tabdet_onSymbolSave(object sender, MapViewerEx.SaveSymbolEventArgs e)
        {
            if (sender is MapViewerEx)
            {
                // juiste filename kiezen 
                MapViewerEx tabdet = (MapViewerEx)sender;
                string note = string.Empty;
                if (m_appSettings.RequestProjectNotes && Tools.Instance.m_CurrentWorkingProject != "")
                {
                    //request a small note from the user in which he/she can denote a description of the change
                    frmChangeNote changenote = new frmChangeNote();
                    changenote.ShowDialog();
                    note = changenote.Note;
                }

                SaveDataIncludingSyncOption(e.Filename, e.SymbolName, e.SymbolAddress, e.SymbolLength, e.SymbolDate, true, note);
                
            }
        }

        private void SaveDataIncludingSyncOption(string fileName, string varName, int address, int length, byte[] data, bool useNote, string note)
        {
            Tools.Instance.savedatatobinary(address, length, data, fileName, useNote, note, Tools.Instance.m_currentFileType);
            if (m_appSettings.CodeBlockSyncActive)
            {
                // check for other symbols with the same length and the same END address
                if (fileName == Tools.Instance.m_currentfile)
                {
                    
                    int codeBlockOffset = -1;
                    foreach (SymbolHelper sh in Tools.Instance.m_symbols)
                    {
                        if (sh.Flash_start_address == address && sh.Length == length)
                        {
                            if (sh.CodeBlock > 0)
                            {
                                foreach (CodeBlock cb in Tools.Instance.codeBlockList)
                                {
                                    if (cb.CodeID == sh.CodeBlock)
                                    {
                                        codeBlockOffset = address - cb.StartAddress;
                                        break;
                                    }
                                }
                            }
                            break;
                            
                        }
                    }
                    foreach (SymbolHelper sh in Tools.Instance.m_symbols)
                    {
                        bool shSaved = false;
                        if (sh.Length == length)
                        {
                            if (sh.Flash_start_address != address)
                            {
                                if ((sh.Flash_start_address & 0x0FFFF) == (address & 0x0FFFF))
                                {
                                    // 
                                    // if (MessageBox.Show("Do you want to save " + sh.Varname + " at address " + sh.Flash_start_address.ToString("X8") + " as well?", "Codeblock synchronizer", MessageBoxButtons.YesNo) == DialogResult.Yes)
                                    {
                                        Tools.Instance.savedatatobinary((int)sh.Flash_start_address, length, data, fileName, useNote, note, Tools.Instance.m_currentFileType);
                                        shSaved = true;
                                    }
                                }
                                // also check wether codeblock start + offset is equal
                            }
                        }
                        if (!shSaved && codeBlockOffset >= 0)
                        {
                            if (sh.Length == length)
                            {
                                if (sh.Flash_start_address != address)
                                {
                                    // determine codeblock offset for this symbol
                                    if (sh.CodeBlock > 0)
                                    {
                                        foreach (CodeBlock cb in Tools.Instance.codeBlockList)
                                        {
                                            if (cb.CodeID == sh.CodeBlock)
                                            {
                                                int thiscodeBlockOffset = (int)sh.Flash_start_address - cb.StartAddress;
                                                if (thiscodeBlockOffset == codeBlockOffset)
                                                {
                                                    // save this as well
                                                    Tools.Instance.savedatatobinary((int)sh.Flash_start_address, length, data, fileName, useNote, note, Tools.Instance.m_currentFileType);
                                                }
                                                break;
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            UpdateRollbackForwardControls();
            VerifyChecksum(fileName, false, false);
        }

        private void SaveAxisDataIncludingSyncOption(int address, int length, byte[] data, string fileName, bool useNote, string note)
        {
            Tools.Instance.savedatatobinary(address, length, data, fileName, useNote, note, Tools.Instance.m_currentFileType);
            if (m_appSettings.CodeBlockSyncActive)
            {
                // check for other symbols with the same length and the same END address
                if (fileName == Tools.Instance.m_currentfile)
                {
                    foreach (SymbolHelper sh in Tools.Instance.m_symbols)
                    {
                        if (sh.X_axis_address != address)
                        {
                            if ((sh.X_axis_address & 0x0FFFF) == (address & 0x0FFFF))
                            {
                                if (sh.X_axis_length * 2 == length)
                                {
                                    Tools.Instance.savedatatobinary(sh.X_axis_address, length, data, fileName, useNote, note, Tools.Instance.m_currentFileType);
                                }
                            }
                        }
                        else if (sh.Y_axis_address != address)
                        {
                            if ((sh.Y_axis_address & 0x0FFFF) == (address & 0x0FFFF))
                            {
                                if (sh.Y_axis_length * 2 == length)
                                {
                                    Tools.Instance.savedatatobinary(sh.Y_axis_address, length, data, fileName, useNote, note, Tools.Instance.m_currentFileType);
                                }
                            }
                        }
                    }
                }
            }
            UpdateRollbackForwardControls();

            VerifyChecksum(Tools.Instance.m_currentfile, false, false);
        }


        

        private void VerifyChecksum(string filename, bool showQuestion, bool showInfo)
        {
            
            string chkType = string.Empty;
            barChecksum.Caption = "---";
            ChecksumResultDetails result = new ChecksumResultDetails();
            if (m_appSettings.AutoChecksum)
            {
                result = Tools.Instance.UpdateChecksum(filename, false);
                if (showInfo)
                {
                    if (result.CalculationOk)
                    {
                        if (result.TypeResult == ChecksumType.VAG_EDC15P_V41) chkType = " V4.1";
                        else if (result.TypeResult == ChecksumType.VAG_EDC15P_V41V2) chkType = " V4.1v2";
                        else if (result.TypeResult == ChecksumType.VAG_EDC15P_V41_2002) chkType = " V4.1 2002";
                        else if (result.TypeResult != ChecksumType.Unknown) chkType = result.TypeResult.ToString();
                        frmInfoBox info = new frmInfoBox("Checksums are correct [" + chkType + "]");
                    }
                    else
                    {
                        if (result.TypeResult == ChecksumType.VAG_EDC15P_V41) chkType = " V4.1";
                        else if (result.TypeResult == ChecksumType.VAG_EDC15P_V41V2) chkType = " V4.1v2";
                        else if (result.TypeResult == ChecksumType.VAG_EDC15P_V41_2002) chkType = " V4.1 2002";
                        else if (result.TypeResult != ChecksumType.Unknown) chkType = result.TypeResult.ToString();
                        frmInfoBox info = new frmInfoBox("Checksums are INCORRECT [" + chkType + "]");

                    }
                }
            }
            else
            {
                result = Tools.Instance.UpdateChecksum(filename, true);
                if (!result.CalculationOk)
                {
                    if (showQuestion && result.TypeResult != ChecksumType.Unknown)
                    {
                         if (result.TypeResult == ChecksumType.VAG_EDC15P_V41) chkType = " V4.1";
                         else if (result.TypeResult == ChecksumType.VAG_EDC15P_V41V2) chkType = " V4.1v2";
                         else if (result.TypeResult == ChecksumType.VAG_EDC15P_V41_2002) chkType = " V4.1 2002";
                         else if (result.TypeResult != ChecksumType.Unknown) chkType = result.TypeResult.ToString();
                        frmChecksumIncorrect frmchk = new frmChecksumIncorrect();
                        frmchk.ChecksumType = chkType;
                        frmchk.NumberChecksums = result.NumberChecksumsTotal;
                        frmchk.NumberChecksumsFailed = result.NumberChecksumsFail;
                        frmchk.NumberChecksumsPassed = result.NumberChecksumsOk;
                        if(frmchk.ShowDialog() == DialogResult.OK)
                        //if (MessageBox.Show("Checksums are invalid. Do you wish to correct them?", "Warning", MessageBoxButtons.YesNo) == DialogResult.Yes)
                        {
                            result = Tools.Instance.UpdateChecksum(filename, false);
                        }
                    }
                    else if (showInfo && result.TypeResult == ChecksumType.Unknown)
                    {
                        frmInfoBox info = new frmInfoBox("Checksum for this filetype is not yet implemented");
                    }
                }
                else
                {
                    if (showInfo)
                    {
                        if (result.TypeResult == ChecksumType.VAG_EDC15P_V41) chkType = " V4.1";
                        else if (result.TypeResult == ChecksumType.VAG_EDC15P_V41V2) chkType = " V4.1v2";
                        else if (result.TypeResult == ChecksumType.VAG_EDC15P_V41_2002) chkType = " V4.1 2002";
                        else if (result.TypeResult != ChecksumType.Unknown) chkType = result.TypeResult.ToString();
                        frmInfoBox info = new frmInfoBox("Checksums are correct [" + chkType + "]");
                    }
                }
            }

            if (result.TypeResult == ChecksumType.VAG_EDC15P_V41) chkType = " V4.1";
            else if (result.TypeResult == ChecksumType.VAG_EDC15P_V41V2) chkType = " V4.1v2";
            else if (result.TypeResult == ChecksumType.VAG_EDC15P_V41_2002) chkType = " V4.1 2002";
            if (!result.CalculationOk)
            {
                barChecksum.Caption = "Checksum failed" + chkType;
            }
            else
            {
                barChecksum.Caption = "Checksum Ok" + chkType;
            }
            Application.DoEvents();
        }

        

        

        private double GetMapCorrectionOffset(string mapname)
        {
            return 0;
        }

        private int GetTableMatrixWitdhByName(string filename, SymbolCollection curSymbols, string symbolname, out int columns, out int rows)
        {
            columns = GetSymbolWidth(curSymbols, symbolname);
            rows = GetSymbolHeight(curSymbols, symbolname);
            return columns;
        }

        private int GetSymbolWidth(SymbolCollection curSymbolCollection, string symbolname)
        {
            foreach (SymbolHelper sh in curSymbolCollection)
            {
                if (sh.Varname == symbolname || sh.Userdescription == symbolname)
                {
                    return sh.Y_axis_length;
                }
            }
            return 0;
        }

        private int GetSymbolHeight(SymbolCollection curSymbolCollection, string symbolname)
        {
            foreach (SymbolHelper sh in curSymbolCollection)
            {
                if (sh.Varname == symbolname || sh.Userdescription == symbolname)
                {
                    return sh.X_axis_length;
                }
            }
            return 0;
        }

        private int GetSymbolLength(SymbolCollection curSymbolCollection, string symbolname)
        {
            foreach (SymbolHelper sh in curSymbolCollection)
            {
                if (sh.Varname == symbolname || sh.Userdescription == symbolname)
                {
                    return sh.Length;
                }
            }
            return 0;
        }
        private Int64 GetSymbolAddress(SymbolCollection curSymbolCollection, string symbolname)
        {
            foreach (SymbolHelper sh in curSymbolCollection)
            {
                if (sh.Varname == symbolname || sh.Userdescription == symbolname)
                {
                    return sh.Flash_start_address;
                }
            }
            return 0;
        }

        private double GetMapCorrectionFactor(string symbolname)
        {
            double returnvalue = 1;
            
            return returnvalue;
        }



        

        private int[] GetYaxisValues(string filename, SymbolCollection curSymbols, string symbolname)
        {
            int xlen = GetSymbolHeight(curSymbols, symbolname);
            int xaddress = GetXAxisAddress(curSymbols, symbolname);
            int[] retval = new int[xlen];
            retval.Initialize();
            if(xaddress > 0)
            {
                retval = Tools.Instance.readdatafromfileasint(filename, xaddress, xlen, Tools.Instance.m_currentFileType);
            }
            return retval;
        }

        private int GetXAxisAddress(SymbolCollection curSymbols, string symbolname)
        {
            foreach (SymbolHelper sh in curSymbols)
            {
                if (sh.Varname == symbolname || sh.Userdescription == symbolname)
                {
                    return sh.X_axis_address;
                }
            }
            return 0;
        }

        private int GetYAxisAddress(SymbolCollection curSymbols, string symbolname)
        {
            foreach (SymbolHelper sh in curSymbols)
            {
                if (sh.Varname == symbolname || sh.Userdescription == symbolname)
                {
                    return sh.Y_axis_address;
                }
            }
            return 0;
        }
        private int[] GetXaxisValues(string filename, SymbolCollection curSymbols, string symbolname)
        {
            int ylen = GetSymbolWidth(curSymbols, symbolname);
            int yaddress = GetYAxisAddress(curSymbols, symbolname);
            int[] retval = new int[ylen];
            retval.Initialize();
            if (yaddress > 0)
            {
                retval = Tools.Instance.readdatafromfileasint(filename, yaddress, ylen, Tools.Instance.m_currentFileType);
            }
            return retval;

        }

        void dockPanel_ClosedPanel(object sender, DevExpress.XtraBars.Docking.DockPanelEventArgs e)
        {
            if (sender is DockPanel)
            {
                DockPanel pnl = (DockPanel)sender;

                foreach (Control c in pnl.Controls)
                {
                    if (c is HexViewer)
                    {
                        HexViewer vwr = (HexViewer)c;
                        vwr.CloseFile();
                    }
                    else if (c is DevExpress.XtraBars.Docking.DockPanel)
                    {
                        DevExpress.XtraBars.Docking.DockPanel tpnl = (DevExpress.XtraBars.Docking.DockPanel)c;
                        foreach (Control c2 in tpnl.Controls)
                        {
                            if (c2 is HexViewer)
                            {
                                HexViewer vwr2 = (HexViewer)c2;
                                vwr2.CloseFile();
                            }
                        }
                    }
                    else if (c is DevExpress.XtraBars.Docking.ControlContainer)
                    {
                        DevExpress.XtraBars.Docking.ControlContainer cntr = (DevExpress.XtraBars.Docking.ControlContainer)c;
                        foreach (Control c3 in cntr.Controls)
                        {
                            if (c3 is HexViewer)
                            {
                                HexViewer vwr3 = (HexViewer)c3;
                                vwr3.CloseFile();
                            }
                        }
                    }
                }
                dockManager1.RemovePanel(pnl);
            }
        }

        private void btnCompareFiles_ItemClick(object sender, DevExpress.XtraBars.ItemClickEventArgs e)
        {
            OpenFileDialog openFileDialog1 = new OpenFileDialog();
            //openFileDialog1.Filter = "Binaries|*.bin;*.ori";
            openFileDialog1.Multiselect = false;
            if (openFileDialog1.ShowDialog() == DialogResult.OK)
            {
                string compareFile = openFileDialog1.FileName;
                CompareToFile(compareFile);

            }
        }

        private void CompareToFile(string filename)
        {
            if (Tools.Instance.m_symbols.Count > 0)
            {
                dockManager1.BeginUpdate();
                try
                {
                    DevExpress.XtraBars.Docking.DockPanel dockPanel = dockManager1.AddPanel(new System.Drawing.Point(-500, -500));
                    CompareResults tabdet = new CompareResults();
                    tabdet.ShowAddressesInHex = true;
                    tabdet.SetFilterMode(true);
                    tabdet.Dock = DockStyle.Fill;
                    tabdet.Filename = filename;
                    tabdet.onSymbolSelect += new CompareResults.NotifySelectSymbol(tabdet_onSymbolSelect);
                    dockPanel.Controls.Add(tabdet);
                    dockPanel.Text = "Compare results: " + Path.GetFileName(filename);
                    dockPanel.DockTo(dockManager1, DevExpress.XtraBars.Docking.DockingStyle.Left, 1);
                    dockPanel.Width = 500;
                    SymbolCollection compare_symbols = new SymbolCollection();
                    List<CodeBlock> compare_blocks = new List<CodeBlock>();
                    List<AxisHelper> compare_axis = new List<AxisHelper>();
                    compare_symbols = DetectMaps(filename, out compare_blocks, out compare_axis, false, false);
                    System.Windows.Forms.Application.DoEvents();

                    Console.WriteLine("ori : " + Tools.Instance.m_symbols.Count.ToString());
                    Console.WriteLine("comp : " + compare_symbols.Count.ToString());

                    System.Data.DataTable dt = new System.Data.DataTable();
                    dt.Columns.Add("SYMBOLNAME");
                    dt.Columns.Add("SRAMADDRESS", Type.GetType("System.Int32"));
                    dt.Columns.Add("FLASHADDRESS", Type.GetType("System.Int32"));
                    dt.Columns.Add("LENGTHBYTES", Type.GetType("System.Int32"));
                    dt.Columns.Add("LENGTHVALUES", Type.GetType("System.Int32"));
                    dt.Columns.Add("DESCRIPTION");
                    dt.Columns.Add("ISCHANGED", Type.GetType("System.Boolean"));
                    dt.Columns.Add("CATEGORY", Type.GetType("System.Int32")); //0
                    dt.Columns.Add("DIFFPERCENTAGE", Type.GetType("System.Double"));
                    dt.Columns.Add("DIFFABSOLUTE", Type.GetType("System.Int32"));
                    dt.Columns.Add("DIFFAVERAGE", Type.GetType("System.Double"));
                    dt.Columns.Add("CATEGORYNAME");
                    dt.Columns.Add("SUBCATEGORYNAME");
                    dt.Columns.Add("SymbolNumber1", Type.GetType("System.Int32"));
                    dt.Columns.Add("SymbolNumber2", Type.GetType("System.Int32"));
                    dt.Columns.Add("Userdescription");
                    dt.Columns.Add("MissingInOriFile", Type.GetType("System.Boolean"));
                    dt.Columns.Add("MissingInCompareFile", Type.GetType("System.Boolean"));
                    dt.Columns.Add("CodeBlock1", Type.GetType("System.Int32"));
                    dt.Columns.Add("CodeBlock2", Type.GetType("System.Int32"));
                    string category = "";
                    string ht = string.Empty;
                    double diffperc = 0;
                    int diffabs = 0;
                    double diffavg = 0;
                    int percentageDone = 0;
                    int symNumber = 0;
                    XDFCategories cat = XDFCategories.Undocumented;
                    XDFSubCategory subcat = XDFSubCategory.Undocumented;
                    if (compare_symbols.Count > 0)
                    {
                        CompareResults cr = new CompareResults();
                        cr.ShowAddressesInHex = true;
                        cr.SetFilterMode(true);
                        foreach (SymbolHelper sh_compare in compare_symbols)
                        {
                            foreach (SymbolHelper sh_org in Tools.Instance.m_symbols)
                            {
                                if ((sh_compare.Flash_start_address == sh_org.Flash_start_address) || (sh_compare.Varname == sh_org.Varname))
                                {
                                    // compare
                                    if (!CompareSymbolToCurrentFile(sh_compare.Varname, (int)sh_compare.Flash_start_address, sh_compare.Length, filename, out diffperc, out diffabs, out diffavg, sh_compare.Correction))
                                    {
                                        dt.Rows.Add(sh_compare.Varname, sh_compare.Start_address, sh_compare.Flash_start_address, sh_compare.Length, sh_compare.Length, sh_compare.Varname, false, 0, diffperc, diffabs, diffavg, category, "", sh_org.Symbol_number, sh_compare.Symbol_number, "", false, false, sh_org.CodeBlock, sh_compare.CodeBlock);

                                    }
                                }
                            }
                        }
                        
                        tabdet.CompareSymbolCollection = compare_symbols;
                        tabdet.OriginalSymbolCollection = Tools.Instance.m_symbols;
                        tabdet.OriginalFilename = Tools.Instance.m_currentfile;
                        tabdet.CompareFilename = filename;
                        tabdet.OpenGridViewGroups(tabdet.gridControl1, 1);
                        tabdet.gridControl1.DataSource = dt.Copy();
                    }
                }
                catch (Exception E)
                {
                    Console.WriteLine(E.Message);
                }
                dockManager1.EndUpdate();
            }
        }
        private bool SymbolExists(string symbolname)
        {
            foreach (SymbolHelper sh in Tools.Instance.m_symbols)
            {
                if (sh.Varname == symbolname || sh.Userdescription == symbolname) return true;
            }
            return false;
        }

        private void StartCompareMapViewer(string SymbolName, string Filename, int SymbolAddress, int SymbolLength, SymbolCollection curSymbols, int symbolnumber)
        {
            try
            {
                SymbolHelper sh = FindSymbol(curSymbols, SymbolName);
                
                DevExpress.XtraBars.Docking.DockPanel dockPanel;
                bool pnlfound = false;
                foreach (DevExpress.XtraBars.Docking.DockPanel pnl in dockManager1.Panels)
                {

                    if (pnl.Text == "Symbol: " + SymbolName + " [" + Path.GetFileName(Filename) + "]")
                    {
                        if (pnl.Tag.ToString() == Filename) // <GS-10052011>
                        {
                            dockPanel = pnl;
                            pnlfound = true;
                            dockPanel.Show();
                        }
                    }
                }
                if (!pnlfound)
                {
                    dockManager1.BeginUpdate();
                    try
                    {
                        dockPanel = dockManager1.AddPanel(new System.Drawing.Point(-500, -500));
                        dockPanel.Tag = Filename;// Tools.Instance.m_currentfile; changed 24/01/2008
                        MapViewerEx tabdet = new MapViewerEx();

                        tabdet.AutoUpdateIfSRAM = false;// m_appSettings.AutoUpdateSRAMViewers;
                        tabdet.AutoUpdateInterval = 99999;
                        tabdet.SetViewSize(ViewSize.NormalView);

                        //tabdet.IsHexMode = barViewInHex.Checked;
                        tabdet.Viewtype = m_appSettings.DefaultViewType;
                        tabdet.DisableColors = m_appSettings.DisableMapviewerColors;
                        tabdet.AutoSizeColumns = m_appSettings.AutoSizeColumnsInWindows;
                        tabdet.GraphVisible = m_appSettings.ShowGraphs;
                        tabdet.IsRedWhite = m_appSettings.ShowRedWhite;
                        tabdet.SetViewSize(m_appSettings.DefaultViewSize);
                        tabdet.Filename = Filename;
                        tabdet.Map_name = SymbolName;
                        tabdet.Map_descr = tabdet.Map_name;
                        tabdet.Map_cat = XDFCategories.Undocumented;
                        tabdet.X_axisvalues = GetXaxisValues(Filename, curSymbols, tabdet.Map_name);
                        tabdet.Y_axisvalues = GetYaxisValues(Filename, curSymbols, tabdet.Map_name);

                        SymbolAxesTranslator axestrans = new SymbolAxesTranslator();
                        string x_axis = string.Empty;
                        string y_axis = string.Empty;
                        string x_axis_descr = string.Empty;
                        string y_axis_descr = string.Empty;
                        string z_axis_descr = string.Empty;

                        tabdet.X_axis_name = sh.X_axis_descr;
                        tabdet.Y_axis_name = sh.Y_axis_descr;
                        tabdet.Z_axis_name = sh.Z_axis_descr;
                        tabdet.XaxisUnits = sh.XaxisUnits;
                        tabdet.YaxisUnits = sh.YaxisUnits;
                        tabdet.X_axisAddress = sh.Y_axis_address;
                        tabdet.Y_axisAddress = sh.X_axis_address;

                        tabdet.Xaxiscorrectionfactor = sh.X_axis_correction;
                        tabdet.Yaxiscorrectionfactor = sh.Y_axis_correction;

                        //tabdet.X_axisvalues = GetXaxisValues(Tools.Instance.m_currentfile, curSymbols, tabdet.Map_name);
                        //tabdet.Y_axisvalues = GetYaxisValues(Tools.Instance.m_currentfile, curSymbols, tabdet.Map_name);

                        int columns = 8;
                        int rows = 8;
                        int tablewidth = GetTableMatrixWitdhByName(Filename, curSymbols, tabdet.Map_name, out columns, out rows);
                        int address = Convert.ToInt32(SymbolAddress);
                        if (address != 0)
                        {
                            tabdet.Map_address = address;
                            int length = SymbolLength;
                            tabdet.Map_length = length;
                            byte[] mapdata = Tools.Instance.readdatafromfile(Filename, address, length, Tools.Instance.m_currentFileType);
                            tabdet.Map_content = mapdata;
                            tabdet.Correction_factor = sh.Correction;
                            tabdet.Correction_offset = sh.Offset;// GetMapCorrectionOffset(tabdet.Map_name);
                            tabdet.IsUpsideDown = m_appSettings.ShowTablesUpsideDown;
                            tabdet.ShowTable(columns, true);
                            tabdet.Dock = DockStyle.Fill;
                            tabdet.onSymbolSave += new VAGSuite.MapViewerEx.NotifySaveSymbol(tabdet_onSymbolSave);
                            tabdet.onSymbolRead += new VAGSuite.MapViewerEx.NotifyReadSymbol(tabdet_onSymbolRead);
                            tabdet.onClose += new VAGSuite.MapViewerEx.ViewerClose(tabdet_onClose);

                            tabdet.onSliderMove += new MapViewerEx.NotifySliderMove(tabdet_onSliderMove);
                            tabdet.onSplitterMoved += new MapViewerEx.SplitterMoved(tabdet_onSplitterMoved);
                            tabdet.onSelectionChanged += new MapViewerEx.SelectionChanged(tabdet_onSelectionChanged);
                            tabdet.onSurfaceGraphViewChangedEx += new MapViewerEx.SurfaceGraphViewChangedEx(tabdet_onSurfaceGraphViewChangedEx);
                            tabdet.onAxisLock += new MapViewerEx.NotifyAxisLock(tabdet_onAxisLock);
                            tabdet.onViewTypeChanged += new MapViewerEx.ViewTypeChanged(tabdet_onViewTypeChanged);


                            //dockPanel.DockAsTab(dockPanel1);
                            dockPanel.Text = "Symbol: " + SymbolName + " [" + Path.GetFileName(Filename) + "]";
                            dockPanel.DockTo(dockManager1, DevExpress.XtraBars.Docking.DockingStyle.Right, 1);
                            bool isDocked = false;
                            // Try to dock to same symbol
                            foreach (DevExpress.XtraBars.Docking.DockPanel pnl in dockManager1.Panels)
                            {
                                if (pnl.Text.StartsWith("Symbol: " + SymbolName) && pnl != dockPanel && (pnl.Visibility == DevExpress.XtraBars.Docking.DockVisibility.Visible))
                                {
                                    dockPanel.DockAsTab(pnl, 0);
                                    isDocked = true;
                                    break;
                                }
                            }
                            if (!isDocked)
                            {
                                int width = 500;
                                if (tabdet.X_axisvalues.Length > 0)
                                {
                                    width = 30 + ((tabdet.X_axisvalues.Length + 1) * 45);
                                }
                                if (width < 500) width = 500;
                                if (width > 800) width = 800;

                                dockPanel.Width = width;
                            }
                            dockPanel.Controls.Add(tabdet);
                        }
                    }
                    catch (Exception E)
                    {
                        Console.WriteLine(E.Message);
                    }
                    dockManager1.EndUpdate();
                    System.Windows.Forms.Application.DoEvents();
                }
            }
            catch (Exception startnewcompareE)
            {
                Console.WriteLine(startnewcompareE.Message);
            }

        }

        private SymbolHelper FindSymbol(SymbolCollection curSymbols, string SymbolName)
        {
            foreach (SymbolHelper sh in curSymbols)
            {
                if (sh.Varname == SymbolName || sh.Userdescription == SymbolName) return sh;
            }
            return new SymbolHelper();
        }

        void tabdet_onSymbolSelect(object sender, CompareResults.SelectSymbolEventArgs e)
        {
            
            if (!e.ShowDiffMap)
            {
                DumpDockWindows();
                if (SymbolExists(e.SymbolName))
                {
                    StartTableViewer(e.SymbolName, e.CodeBlock1);
                }
                //DumpDockWindows();
                foreach (SymbolHelper sh in e.Symbols)
                {
                    if (sh.Varname == e.SymbolName || sh.Userdescription == e.SymbolName)
                    {
                        string symName = e.SymbolName;
                        if ((e.SymbolName.StartsWith("2D") || e.SymbolName.StartsWith("3D"))  && sh.Userdescription != string.Empty) symName = sh.Userdescription;
                        StartCompareMapViewer(symName, e.Filename, e.SymbolAddress, e.SymbolLength, e.Symbols, e.Symbolnumber2);
                        break;
                    }
                }
                DumpDockWindows();
            }
            else
            {
                // show difference map
                foreach (SymbolHelper sh in e.Symbols)
                {
                    if (sh.Varname == e.SymbolName || sh.Userdescription == e.SymbolName)
                    {
                        StartCompareDifferenceViewer(sh, e.Filename, e.SymbolAddress);
                        break;
                    }
                }
                
            }
        }

        private void StartCompareDifferenceViewer(SymbolHelper sh, string Filename, int SymbolAddress)
        {
            DevExpress.XtraBars.Docking.DockPanel dockPanel;
            bool pnlfound = false;
            foreach (DevExpress.XtraBars.Docking.DockPanel pnl in dockManager1.Panels)
            {

                if (pnl.Text == "Symbol difference: " + sh.Varname + " [" + Path.GetFileName(Tools.Instance.m_currentfile) + "]")
                {
                    dockPanel = pnl;
                    pnlfound = true;
                    dockPanel.Show();
                }
            }
            if (!pnlfound)
            {
                dockManager1.BeginUpdate();
                try
                {
                    dockPanel = dockManager1.AddPanel(new System.Drawing.Point(-500, -500));
                    dockPanel.Tag = Tools.Instance.m_currentfile;
                    MapViewerEx tabdet = new MapViewerEx();
                    tabdet.Map_name = sh.Varname;
                    tabdet.IsDifferenceViewer = true;
                    tabdet.AutoUpdateIfSRAM = false;
                    tabdet.AutoUpdateInterval = 999999;
                    tabdet.Viewtype = m_appSettings.DefaultViewType;
                    tabdet.DisableColors = m_appSettings.DisableMapviewerColors;
                    tabdet.AutoSizeColumns = m_appSettings.AutoSizeColumnsInWindows;
                    tabdet.GraphVisible = m_appSettings.ShowGraphs;
                    tabdet.IsRedWhite = m_appSettings.ShowRedWhite;
                    tabdet.SetViewSize(m_appSettings.DefaultViewSize);
                    tabdet.Filename = Filename;
                    tabdet.Map_descr = tabdet.Map_name;
                    tabdet.Map_cat = XDFCategories.Undocumented;
                    tabdet.X_axisvalues = GetXaxisValues(Tools.Instance.m_currentfile, Tools.Instance.m_symbols, tabdet.Map_name);
                    tabdet.Y_axisvalues = GetYaxisValues(Tools.Instance.m_currentfile, Tools.Instance.m_symbols, tabdet.Map_name);

                    SymbolAxesTranslator axestrans = new SymbolAxesTranslator();
                    string x_axis = string.Empty;
                    string y_axis = string.Empty;
                    string x_axis_descr = string.Empty;
                    string y_axis_descr = string.Empty;
                    string z_axis_descr = string.Empty;

                    tabdet.X_axis_name = sh.X_axis_descr;
                    tabdet.Y_axis_name = sh.Y_axis_descr;
                    tabdet.Z_axis_name = sh.Z_axis_descr;
                    tabdet.XaxisUnits = sh.XaxisUnits;
                    tabdet.YaxisUnits = sh.YaxisUnits;
                    tabdet.X_axisAddress = sh.Y_axis_address;
                    tabdet.Y_axisAddress = sh.X_axis_address;

                    tabdet.Xaxiscorrectionfactor = sh.X_axis_correction;
                    tabdet.Yaxiscorrectionfactor = sh.Y_axis_correction;

                    tabdet.X_axisvalues = GetXaxisValues(Tools.Instance.m_currentfile, Tools.Instance.m_symbols, tabdet.Map_name);
                    tabdet.Y_axisvalues = GetYaxisValues(Tools.Instance.m_currentfile, Tools.Instance.m_symbols, tabdet.Map_name);
                    

                    //tabdet.Map_sramaddress = GetSymbolAddressSRAM(SymbolName);
                    int columns = 8;
                    int rows = 8;
                    int tablewidth = GetTableMatrixWitdhByName(Tools.Instance.m_currentfile, Tools.Instance.m_symbols, tabdet.Map_name, out columns, out rows);
                    int address = Convert.ToInt32(SymbolAddress);
                    if (address != 0)
                    {
                        tabdet.Map_address = address;
                        int length = sh.Length;
                        tabdet.Map_length = length;
                        byte[] mapdata = Tools.Instance.readdatafromfile(Filename, address, length, Tools.Instance.m_currentFileType);
                        byte[] mapdataorig = Tools.Instance.readdatafromfile(Filename, address, length, Tools.Instance.m_currentFileType);
                        byte[] mapdata2 = Tools.Instance.readdatafromfile(Tools.Instance.m_currentfile, (int)GetSymbolAddress(Tools.Instance.m_symbols, sh.Varname), GetSymbolLength(Tools.Instance.m_symbols, sh.Varname), Tools.Instance.m_currentFileType);

                        tabdet.Map_original_content = mapdataorig;
                        tabdet.Map_compare_content = mapdata2;

                        if (mapdata.Length == mapdata2.Length)
                        {

                            for (int bt = 0; bt < mapdata2.Length; bt += 2)
                            {
                                int value1 = Convert.ToInt16(mapdata.GetValue(bt)) * 256 + Convert.ToInt16(mapdata.GetValue(bt + 1));
                                int value2 = Convert.ToInt16(mapdata2.GetValue(bt)) * 256 + Convert.ToInt16(mapdata2.GetValue(bt + 1));
                                value1 = Math.Abs((int)value1 - (int)value2);
                                byte v1 = (byte)(value1 / 256);
                                byte v2 = (byte)(value1 - (int)v1 * 256);
                                mapdata.SetValue(v1, bt);
                                mapdata.SetValue(v2, bt + 1);
                            }


                            tabdet.Map_content = mapdata;
                            tabdet.UseNewCompare = true;
                            tabdet.Correction_factor = sh.Correction;
                            tabdet.Correction_offset = sh.Offset;
                            tabdet.IsUpsideDown = m_appSettings.ShowTablesUpsideDown;
                            tabdet.ShowTable(columns, true);
                            tabdet.Dock = DockStyle.Fill;
                            tabdet.onClose += new MapViewerEx.ViewerClose(tabdet_onClose);
                            dockPanel.Text = "Symbol difference: " + sh.Varname + " [" + Path.GetFileName(Filename) + "]";
                            bool isDocked = false;

                            if (!isDocked)
                            {
                                dockPanel.DockTo(dockManager1, DevExpress.XtraBars.Docking.DockingStyle.Right, 0);
                                if (m_appSettings.AutoSizeNewWindows)
                                {
                                    if (tabdet.X_axisvalues.Length > 0)
                                    {
                                        dockPanel.Width = 30 + ((tabdet.X_axisvalues.Length + 1) * 45);
                                    }
                                    else
                                    {
                                        //dockPanel.Width = this.Width - dockSymbols.Width - 10;

                                    }
                                }
                                if (dockPanel.Width < 400) dockPanel.Width = 400;

                                //                    dockPanel.Width = 400;
                            }
                            dockPanel.Controls.Add(tabdet);

                        }
                        else
                        {
                            frmInfoBox info = new frmInfoBox("Map lengths don't match...");
                        }
                    }
                }
                catch (Exception E)
                {

                    Console.WriteLine(E.Message);
                }
                dockManager1.EndUpdate();
            }
        }

        private void DumpDockWindows()
        {
            foreach(DockPanel dp in dockManager1.Panels)
            {
                Console.WriteLine(dp.Text);

            }
        }


        //private void StartTableViewer(string symbolname, int codeblock)
        //{
        //    int rtel = 0;
        //    bool _vwrstarted = false;
        //    TableViewerStarted = false;
        //    try
        //    {
        //        // Créer une liste de noms alternatifs pour les cartes Audi
        //        List<string> alternativeNames = new List<string>
        //        {
        //            symbolname,
        //            symbolname.Replace(" ", "_"),
        //            symbolname.Replace(" ", ""),
        //            "Audi " + symbolname
        //        };

        //        bool symbolFound = false;
        //        foreach (string altName in alternativeNames)
        //        {
        //            if (Tools.Instance.GetSymbolAddressLike(Tools.Instance.m_symbols, altName) > 0)
        //            {
        //                symbolFound = true;
        //                gridViewSymbols.ActiveFilter.Clear();
        //                gridViewSymbols.ApplyFindFilter("");
        //                SymbolCollection sc = (SymbolCollection)gridControl1.DataSource;
        //                rtel = 0;

        //                // Rechercher d'abord par Varname
        //                foreach (SymbolHelper sh in sc)
        //                {
        //                    if (sh.Varname.StartsWith(altName) && sh.CodeBlock == codeblock)
        //                    {
        //                        try
        //                        {
        //                            SélectionnerSymbole(rtel);
        //                            _vwrstarted = true;
        //                            StartTableViewer();
        //                            break;
        //                        }
        //                        catch (Exception ex)
        //                        {
        //                            MessageBox.Show("Une erreur est survenue lors de la sélection du symbole.", "Erreur", MessageBoxButtons.OK, MessageBoxIcon.Error);
        //                        }
        //                    }
        //                    rtel++;
        //                }

        //                // Si non trouvé, rechercher par Userdescription
        //                if (!_vwrstarted)
        //                {
        //                    rtel = 0;
        //                    foreach (SymbolHelper sh in sc)
        //                    {
        //                        if (sh.Userdescription.StartsWith(altName) && sh.CodeBlock == codeblock)
        //                        {
        //                            try
        //                            {
        //                                SélectionnerSymbole(rtel);
        //                                _vwrstarted = true;
        //                                StartTableViewer();
        //                                break;
        //                            }
        //                            catch (Exception ex)
        //                            {
        //                                MessageBox.Show("Une erreur est survenue lors de la sélection du symbole.", "Erreur", MessageBoxButtons.OK, MessageBoxIcon.Error);
        //                            }
        //                        }
        //                        rtel++;
        //                    }
        //                }

        //                if (_vwrstarted) break;
        //            }
        //        }

        //        // Si aucun nom alternatif ne fonctionne, chercher quand même par Userdescription
        //        if (!symbolFound || !_vwrstarted)
        //        {
        //            gridViewSymbols.ActiveFilter.Clear();
        //            SymbolCollection sc = (SymbolCollection)gridControl1.DataSource;
        //            rtel = 0;

        //            // Essayer avec chaque nom alternatif
        //            foreach (string altName in alternativeNames)
        //            {
        //                foreach (SymbolHelper sh in sc)
        //                {
        //                    // Recherche plus flexible - vérifie si le nom est contenu n'importe où dans la description
        //                    if ((sh.Userdescription.Contains(altName) || sh.Varname.Contains(altName)) && sh.CodeBlock == codeblock)
        //                    {
        //                        try
        //                        {
        //                            SélectionnerSymbole(rtel);
        //                            _vwrstarted = true;
        //                            StartTableViewer();
        //                            break;
        //                        }
        //                        catch (Exception ex)
        //                        {
        //                            MessageBox.Show("Une erreur est survenue lors de la sélection du symbole.", "Erreur", MessageBoxButtons.OK, MessageBoxIcon.Error);
        //                        }
        //                    }
        //                    rtel++;
        //                }

        //                if (_vwrstarted) break;
        //            }
        //        }

        //        // Si toujours rien trouvé, afficher un message explicite
        //        if (!_vwrstarted)
        //        {
        //            TableViewerStarted = true;
        //            MessageBox.Show($"Impossible de trouver la carte '{symbolname}' pour ce véhicule. Elle pourrait être nommée différemment dans ce fichier ECU.",
        //                "Carte non trouvée", MessageBoxButtons.OK, MessageBoxIcon.Information);

        //        }
        //    }
        //    catch (Exception ex)
        //    {
        //        MessageBox.Show("Impossible d'ouvrir la carte. Assurez-vous qu'un fichier est bien chargé.", "Erreur", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        //    }
        //}
        // Modifiez la méthode StartTableViewer pour utiliser un symbole 3D aléatoire si nécessaire
        //private void StartTableViewer(string symbolname, int codeblock)
        //{
        //    int rtel = 0;
        //    bool _vwrstarted = false;
        //    TableViewerStarted = false;

        //    try
        //    {
        //        // Créer une liste de noms alternatifs pour les cartes
        //        List<string> alternativeNames = new List<string>
        //        {
        //            symbolname,
        //            symbolname.Replace(" ", "_"),
        //            symbolname.Replace(" ", ""),
        //            "Audi " + symbolname
        //        };

        //        bool symbolFound = false;
        //        foreach (string altName in alternativeNames)
        //        {
        //            if (Tools.Instance.GetSymbolAddressLike(Tools.Instance.m_symbols, altName) > 0)
        //            {
        //                symbolFound = true;
        //                gridViewSymbols.ActiveFilter.Clear();
        //                gridViewSymbols.ApplyFindFilter("");
        //                SymbolCollection sc = (SymbolCollection)gridControl1.DataSource;
        //                rtel = 0;

        //                // Rechercher d'abord par Varname
        //                foreach (SymbolHelper sh in sc)
        //                {
        //                    if (sh.Varname.StartsWith(altName) && sh.CodeBlock == codeblock)
        //                    {
        //                        try
        //                        {
        //                            SélectionnerSymbole(rtel);
        //                            _vwrstarted = true;
        //                            StartTableViewer();
        //                            break;
        //                        }
        //                        catch (Exception ex)
        //                        {
        //                            MessageBox.Show("Une erreur est survenue lors de la sélection du symbole.", "Erreur", MessageBoxButtons.OK, MessageBoxIcon.Error);
        //                        }
        //                    }
        //                    rtel++;
        //                }

        //                // Si non trouvé, rechercher par Userdescription
        //                if (!_vwrstarted)
        //                {
        //                    rtel = 0;
        //                    foreach (SymbolHelper sh in sc)
        //                    {
        //                        if (sh.Userdescription.StartsWith(altName) && sh.CodeBlock == codeblock)
        //                        {
        //                            try
        //                            {
        //                                SélectionnerSymbole(rtel);
        //                                _vwrstarted = true;
        //                                StartTableViewer();
        //                                break;
        //                            }
        //                            catch (Exception ex)
        //                            {
        //                                MessageBox.Show("Une erreur est survenue lors de la sélection du symbole.", "Erreur", MessageBoxButtons.OK, MessageBoxIcon.Error);
        //                            }
        //                        }
        //                        rtel++;
        //                    }
        //                }

        //                if (_vwrstarted) break;
        //            }
        //        }

        //        // Si aucun symbole n'a été trouvé ou affiché, utiliser un symbole 3D aléatoire
        //        if (!symbolFound || !_vwrstarted)
        //        {
        //            SymbolHelper randomSymbol3D = GetRandom3DSymbol(Tools.Instance.m_symbols);
        //            if (randomSymbol3D != null)
        //            {
        //                // Trouver l'index du symbole 3D aléatoire
        //                SymbolCollection sc = (SymbolCollection)gridControl1.DataSource;
        //                rtel = 0;
        //                foreach (SymbolHelper sh in sc)
        //                {
        //                    if (sh.Flash_start_address == randomSymbol3D.Flash_start_address)
        //                    {
        //                        try
        //                        {
        //                            SélectionnerSymbole(rtel);
        //                            _vwrstarted = true;
        //                            StartTableViewer();
        //                            // Afficher un message informatif discret
        //                            Console.WriteLine($"Symbole '{symbolname}' non trouvé, utilisation du symbole aléatoire 3D: {randomSymbol3D.Varname}");
        //                            break;
        //                        }
        //                        catch (Exception ex)
        //                        {
        //                            Console.WriteLine("Erreur lors de la sélection du symbole 3D aléatoire: " + ex.Message);
        //                        }
        //                    }
        //                    rtel++;
        //                }
        //            }
        //            else
        //            {
        //                TableViewerStarted = true;
        //                MessageBox.Show($"Impossible de trouver la carte '{symbolname}' et aucun symbole 3D n'est disponible.",
        //                    "Carte non trouvée", MessageBoxButtons.OK, MessageBoxIcon.Information);
        //            }
        //        }
        //    }
        //    catch (Exception ex)
        //    {
        //        MessageBox.Show("Impossible d'ouvrir la carte. Assurez-vous qu'un fichier est bien chargé.", "Erreur", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        //    }
        //}
        ////////////////////////////////////////////////////////////////////////////
        ////////////////////////////////////////////////////////////////////////
        /////////////////////////////////////////////////////////////////////////
        //private void StartTableViewer(string symbolname, int codeblock)
        //{
        //    int rtel = 0;
        //    bool _vwrstarted = false;
        //    TableViewerStarted = false;

        //    try
        //    {
        //        SymbolCollection sc = (SymbolCollection)gridControl1.DataSource;
        //        string createdMapName = symbolname ;

        //        // Recherche prioritaire des cartes créées
        //        foreach (SymbolHelper sh in sc)
        //        {
        //            if (sh.Varname == createdMapName)
        //            {
        //                try
        //                {
        //                    SélectionnerSymbole(rtel);
        //                    _vwrstarted = true;
        //                    StartTableViewer();
        //                    return; // Sortie immédiate si on trouve une carte créée
        //                }
        //                catch (Exception ex)
        //                {
        //                    Console.WriteLine("Erreur lors de la sélection de la carte créée: " + ex.Message);
        //                }
        //            }
        //            rtel++;
        //        }

        //        // Créer une liste de noms alternatifs pour les cartes
        //        List<string> alternativeNames = new List<string>
        //        {
        //            symbolname,
        //            symbolname.Replace(" ", "_"),
        //            symbolname.Replace(" ", ""),
        //            "Audi " + symbolname,
        //            "BMW " + symbolname
        //        };

        //        bool symbolFound = false;

        //        // Chercher le symbole avec les noms alternatifs
        //        foreach (string altName in alternativeNames)
        //        {
        //            if (Tools.Instance.GetSymbolAddressLike(Tools.Instance.m_symbols, altName) > 0)
        //            {
        //                symbolFound = true;
        //                gridViewSymbols.ActiveFilter.Clear();
        //                gridViewSymbols.ApplyFindFilter("");
        //                sc = (SymbolCollection)gridControl1.DataSource;
        //                rtel = 0;

        //                // Rechercher d'abord par Varname
        //                foreach (SymbolHelper sh in sc)
        //                {
        //                    if (sh.Varname.StartsWith(altName) && sh.CodeBlock == codeblock)
        //                    {
        //                        try
        //                        {
        //                            SélectionnerSymbole(rtel);
        //                            _vwrstarted = true;
        //                            StartTableViewer();
        //                            break;
        //                        }
        //                        catch (Exception ex)
        //                        {
        //                            MessageBox.Show("Une erreur est survenue lors de la sélection du symbole.", "Erreur", MessageBoxButtons.OK, MessageBoxIcon.Error);
        //                        }
        //                    }
        //                    rtel++;
        //                }

        //                // Si non trouvé, rechercher par Userdescription
        //                if (!_vwrstarted)
        //                {
        //                    rtel = 0;
        //                    foreach (SymbolHelper sh in sc)
        //                    {
        //                        if (sh.Userdescription.StartsWith(altName) && sh.CodeBlock == codeblock)
        //                        {
        //                            try
        //                            {
        //                                SélectionnerSymbole(rtel);
        //                                _vwrstarted = true;
        //                                StartTableViewer();
        //                                break;
        //                            }
        //                            catch (Exception ex)
        //                            {
        //                                MessageBox.Show("Une erreur est survenue lors de la sélection du symbole.", "Erreur", MessageBoxButtons.OK, MessageBoxIcon.Error);
        //                            }
        //                        }
        //                        rtel++;
        //                    }
        //                }

        //                if (_vwrstarted) break;
        //            }
        //        }

        //        // Si aucun symbole n'a été trouvé, créer une carte par défaut
        //        if (!symbolFound || !_vwrstarted)
        //        {
        //            // Demander à l'utilisateur s'il souhaite créer une carte par défaut
        //            if (IsMapAlreadyCreated(symbolname, Tools.Instance.m_symbols))
        //            {
        //                // Chercher et afficher la carte créée
        //                sc = (SymbolCollection)gridControl1.DataSource;
        //                rtel = 0;
        //                foreach (SymbolHelper sh in sc)
        //                {
        //                    if (sh.Varname == symbolname ||
        //                        (sh.Userdescription.Contains(symbolname) && sh.Userdescription.Contains("créé")))
        //                    {
        //                        try
        //                        {
        //                            SélectionnerSymbole(rtel);
        //                            _vwrstarted = true;
        //                            StartTableViewer();
        //                            break;
        //                        }
        //                        catch (Exception ex)
        //                        {
        //                            Console.WriteLine("Erreur lors de la sélection de la carte créée: " + ex.Message);
        //                        }
        //                    }
        //                    rtel++;
        //                }
        //            }
        //            else
        //            {
        //                // Sinon, proposer de créer une nouvelle carte
        //                DialogResult result = MessageBox.Show(
        //                    $"La carte '{symbolname}' n'a pas été trouvée dans ce fichier ECU. " +
        //                    "Voulez-vous créer une carte par défaut avec des valeurs à zéro?",
        //                    "Carte non trouvée", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        //                ////////////////
        //                if (result == DialogResult.Yes)
        //                {
        //                    // Créer la carte par défaut
        //                    CreateDefaultMap(symbolname, Tools.Instance.m_currentfile);

        //                    // Rechercher la nouvelle carte dans la collection mise à jour
        //                    sc = (SymbolCollection)gridControl1.DataSource;
        //                    rtel = 0;
        //                    foreach (SymbolHelper sh in sc)
        //                    {
        //                        if (sh.Varname == symbolname)
        //                        {
        //                            try
        //                            {
        //                                SélectionnerSymbole(rtel);
        //                                _vwrstarted = true;
        //                                StartTableViewer();
        //                                break;
        //                            }
        //                            catch (Exception ex)
        //                            {
        //                                MessageBox.Show("Erreur lors de l'affichage de la carte créée: " + ex.Message,
        //                                    "Erreur", MessageBoxButtons.OK, MessageBoxIcon.Error);
        //                            }
        //                        }
        //                        rtel++;
        //                    }
        //                }
        //                else
        //                {
        //                    TableViewerStarted = true;
        //                    MessageBox.Show($"Opération annulée. Aucune carte n'a été créée pour '{symbolname}'.",
        //                        "Information", MessageBoxButtons.OK, MessageBoxIcon.Information);
        //                }

        //            }
        //        }
        //    }
        //    catch (Exception ex)
        //    {
        //        MessageBox.Show("Impossible d'ouvrir ou de créer la carte: " + ex.Message,
        //            "Erreur", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        //    }
        //}
        /// <summary>
        /// //////////////////////////////////////////////////////////////////////
        /// </summary>
        /// <param name="newSymbol"></param>
        /// <param name="fileName"></param>
        /// 


        private void StartTableViewer(string symbolname, int codeblock)
        {
            int rtel = 0;
            bool _vwrstarted = false;
            TableViewerStarted = false;

            try
            {
                // Amélioration 1: Construire une expression régulière pour détecter les variations du nom
                // Ex: pour "Driver wish", chercher aussi "Driver wish (1)", "Driver wish (2)", etc.
                string searchPattern = "^" + Regex.Escape(symbolname) + @"(\s*\(\d+\))?(\s*\[.*\])?$";
                Regex searchRegex = new Regex(searchPattern, RegexOptions.IgnoreCase);

                // Amélioration 2: Rechercher directement dans les symboles sans filtrer d'abord
                SymbolCollection sc = (SymbolCollection)gridControl1.DataSource;

                // Chercher d'abord la correspondance la plus exacte - celle sans numéro
                foreach (SymbolHelper sh in sc)
                {
                    if (sh.Varname.Equals(symbolname, StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            SélectionnerSymbole(rtel);
                            _vwrstarted = true;
                            StartTableViewer();
                            break;
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine("Erreur lors de la sélection du symbole exact: " + ex.Message);
                        }
                    }
                    rtel++;
                }

                // Si pas trouvé, chercher avec un motif plus large
                if (!_vwrstarted)
                {
                    rtel = 0;
                    foreach (SymbolHelper sh in sc)
                    {
                        // Vérifier si le nom correspond au motif recherché
                        if (searchRegex.IsMatch(sh.Varname))
                        {
                            // Vérifier le code block si nécessaire
                            if (codeblock == 0 || sh.CodeBlock == codeblock)
                            {
                                try
                                {
                                    SélectionnerSymbole(rtel);
                                    _vwrstarted = true;
                                    StartTableViewer();
                                    Console.WriteLine("Correspondance trouvée: " + sh.Varname);
                                    break;
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine("Erreur lors de la sélection du symbole correspondant: " + ex.Message);
                                }
                            }
                        }
                        rtel++;
                    }
                }

                // Si toujours pas trouvé, chercher avec un motif encore plus large
                if (!_vwrstarted)
                {
                    rtel = 0;
                    foreach (SymbolHelper sh in sc)
                    {
                        // Chercher si le nom contient simplement la chaîne recherchée
                        if (sh.Varname.IndexOf(symbolname, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            if (codeblock == 0 || sh.CodeBlock == codeblock)
                            {
                                try
                                {
                                    SélectionnerSymbole(rtel);
                                    _vwrstarted = true;
                                    StartTableViewer();
                                    Console.WriteLine("Correspondance partielle trouvée: " + sh.Varname);
                                    break;
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine("Erreur lors de la sélection du symbole partiel: " + ex.Message);
                                }
                            }
                        }
                        rtel++;
                    }
                }

                // Si toujours rien trouvé, chercher des cartes similaires et ouvrir la première automatiquement
                if (!_vwrstarted)
                {
                    // Collecter toutes les cartes similaires
                    List<int> similairesIndices = new List<int>();

                    rtel = 0;
                    foreach (SymbolHelper sh in sc)
                    {
                        if (sh.Varname.IndexOf(symbolname, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            similairesIndices.Add(rtel);
                            Console.WriteLine("Carte similaire trouvée pour ouverture auto: " + sh.Varname);
                        }
                        rtel++;
                    }

                    // Si des cartes similaires existent, ouvrir automatiquement la première
                    if (similairesIndices.Count > 0)
                    {
                        int indexToSelect = similairesIndices[0]; // Première carte similaire
                        try
                        {
                            SélectionnerSymbole(indexToSelect);
                            _vwrstarted = true;
                            StartTableViewer();
                            Console.WriteLine("Ouverture automatique d'une carte similaire");
                            return; // Sortir de la fonction après l'ouverture réussie
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine("Erreur lors de l'ouverture automatique d'une carte similaire: " + ex.Message);
                        }
                    }

                    // Si vraiment aucune carte similaire n'est trouvée, proposer d'en créer une
                    if (!_vwrstarted)
                    {
                        string message = $"La carte '{symbolname}' n'a pas été trouvée dans ce fichier ECU. " +
                                        "Voulez-vous créer une carte par défaut avec des valeurs à zéro?";

                        DialogResult result = MessageBox.Show(message, "Carte non trouvée",
                                               MessageBoxButtons.YesNo, MessageBoxIcon.Question);

                        if (result == DialogResult.Yes)
                        {
                            // Code existant pour créer une carte
                            CreateDefaultMap(symbolname, Tools.Instance.m_currentfile);
                            // Rechercher la nouvelle carte dans la collection mise à jour
                            sc = (SymbolCollection)gridControl1.DataSource;
                            rtel = 0;
                            foreach (SymbolHelper sh in sc)
                            {
                                if (sh.Varname == symbolname)
                                {
                                    try
                                    {
                                        SélectionnerSymbole(rtel);
                                        _vwrstarted = true;
                                        StartTableViewer();
                                        break;
                                    }
                                    catch (Exception ex)
                                    {
                                        MessageBox.Show("Erreur lors de l'affichage de la carte créée: " + ex.Message,
                                            "Erreur", MessageBoxButtons.OK, MessageBoxIcon.Error);
                                    }
                                }
                                rtel++;
                            }
                        }
                        else
                        {
                            TableViewerStarted = true;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Impossible d'ouvrir ou de créer la carte: " + ex.Message,
                    "Erreur", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
        private void SaveCreatedMapMetadata(SymbolHelper newSymbol, string fileName)
        {
            // Chemin vers le fichier de métadonnées
            string metadataPath = Path.Combine(
                Path.GetDirectoryName(fileName),
                Path.GetFileNameWithoutExtension(fileName) + "_metadata.xml"
            );

            // Créer ou charger la table de métadonnées
            System.Data.DataTable dt = new System.Data.DataTable("CreatedMaps");

            if (File.Exists(metadataPath))
            {
                try
                {
                    dt.ReadXml(metadataPath);
                }
                catch
                {
                    // Si le fichier est corrompu, créer une nouvelle table
                    dt = new System.Data.DataTable("CreatedMaps");
                }
            }

            // Ajouter les colonnes si elles n'existent pas
            if (dt.Columns.Count == 0)
            {
                dt.Columns.Add("SYMBOLNAME");
                dt.Columns.Add("FLASHADDRESS", Type.GetType("System.Int32"));
                dt.Columns.Add("LENGTH", Type.GetType("System.Int32"));
                dt.Columns.Add("XAXISLEN", Type.GetType("System.Int32"));
                dt.Columns.Add("YAXISLEN", Type.GetType("System.Int32"));
                dt.Columns.Add("XAXISADDR", Type.GetType("System.Int32"));
                dt.Columns.Add("YAXISADDR", Type.GetType("System.Int32"));
                dt.Columns.Add("XAXISDESCR");
                dt.Columns.Add("YAXISDESCR");
                dt.Columns.Add("ZAXISDESCR");
                dt.Columns.Add("CORRECTION", Type.GetType("System.Double"));
                dt.Columns.Add("OFFSET", Type.GetType("System.Double"));
                dt.Columns.Add("CATEGORY");
                dt.Columns.Add("SUBCATEGORY");
                dt.Columns.Add("XAXISUNITS");
                dt.Columns.Add("YAXISUNITS");
            }

            // Vérifier si cette carte existe déjà dans les métadonnées
            bool exists = false;
            foreach (DataRow row in dt.Rows)
            {
                if (Convert.ToInt32(row["FLASHADDRESS"]) == newSymbol.Flash_start_address)
                {
                    // Mettre à jour les métadonnées existantes
                    row["SYMBOLNAME"] = newSymbol.Varname;
                    row["LENGTH"] = newSymbol.Length;
                    row["XAXISLEN"] = newSymbol.X_axis_length;
                    row["YAXISLEN"] = newSymbol.Y_axis_length;
                    row["XAXISADDR"] = newSymbol.X_axis_address;
                    row["YAXISADDR"] = newSymbol.Y_axis_address;
                    row["XAXISDESCR"] = newSymbol.X_axis_descr;
                    row["YAXISDESCR"] = newSymbol.Y_axis_descr;
                    row["ZAXISDESCR"] = newSymbol.Z_axis_descr;
                    row["CORRECTION"] = newSymbol.Correction;
                    row["OFFSET"] = newSymbol.Offset;
                    row["CATEGORY"] = newSymbol.Category;
                    row["SUBCATEGORY"] = newSymbol.Subcategory;
                    row["XAXISUNITS"] = newSymbol.XaxisUnits;
                    row["YAXISUNITS"] = newSymbol.YaxisUnits;
                    exists = true;
                    break;
                }
            }

            // Si la carte n'existe pas, l'ajouter
            if (!exists)
            {
                DataRow newRow = dt.NewRow();
                newRow["SYMBOLNAME"] = newSymbol.Varname;
                newRow["FLASHADDRESS"] = newSymbol.Flash_start_address;
                newRow["LENGTH"] = newSymbol.Length;
                newRow["XAXISLEN"] = newSymbol.X_axis_length;
                newRow["YAXISLEN"] = newSymbol.Y_axis_length;
                newRow["XAXISADDR"] = newSymbol.X_axis_address;
                newRow["YAXISADDR"] = newSymbol.Y_axis_address;
                newRow["XAXISDESCR"] = newSymbol.X_axis_descr;
                newRow["YAXISDESCR"] = newSymbol.Y_axis_descr;
                newRow["ZAXISDESCR"] = newSymbol.Z_axis_descr;
                newRow["CORRECTION"] = newSymbol.Correction;
                newRow["OFFSET"] = newSymbol.Offset;
                newRow["CATEGORY"] = newSymbol.Category;
                newRow["SUBCATEGORY"] = newSymbol.Subcategory;
                newRow["XAXISUNITS"] = newSymbol.XaxisUnits;
                newRow["YAXISUNITS"] = newSymbol.YaxisUnits;

                dt.Rows.Add(newRow);
            }

            try
            {
                // Sauvegarder les métadonnées
                dt.WriteXml(metadataPath);
                Console.WriteLine("Métadonnées sauvegardées pour la carte créée: " + newSymbol.Varname);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Erreur lors de la sauvegarde des métadonnées: " + ex.Message);
            }
        }
        private bool CompareSymbolToCurrentFile(string symbolname, int address, int length, string filename, out double diffperc, out int diffabs, out double diffavg, double correction)
        {
            diffperc = 0;
            diffabs = 0;
            diffavg = 0;

            double totalvalue1 = 0;
            double totalvalue2 = 0;
            bool retval = true;

            if (address > 0)
            {
                int curaddress = (int)GetSymbolAddress(Tools.Instance.m_symbols, symbolname);
                int curlength = GetSymbolLength(Tools.Instance.m_symbols, symbolname);
                byte[] curdata = Tools.Instance.readdatafromfile(Tools.Instance.m_currentfile, curaddress, curlength, Tools.Instance.m_currentFileType);
                byte[] compdata = Tools.Instance.readdatafromfile(filename, address, length, Tools.Instance.m_currentFileType);
                if (curdata.Length != compdata.Length)
                {
                    Console.WriteLine("Lengths didn't match: " + symbolname);
                    return false;
                }
                for (int offset = 0; offset < curdata.Length; offset += 2)
                {
                    int ival1 = Convert.ToInt32(curdata.GetValue(offset)) * 256 + Convert.ToInt32(curdata.GetValue(offset + 1));
                    int ival2 = Convert.ToInt32(compdata.GetValue(offset)) * 256 + Convert.ToInt32(compdata.GetValue(offset + 1)) ;
                    if (ival1 != ival2)
                    {
                        retval = false;
                        diffabs++;
                    }
                    totalvalue1 += Convert.ToDouble(ival1);
                    totalvalue2 += Convert.ToDouble(ival2);
                }
                if (curdata.Length > 0)
                {
                    totalvalue1 /= (curdata.Length/2);
                    totalvalue2 /= (compdata.Length/2);
                }
            }
            diffavg = Math.Abs(totalvalue1 - totalvalue2) * correction;
            diffperc = (diffabs * 100) / (length /2);
            return retval;
        }

        private void btnTestFiles_ItemClick(object sender, DevExpress.XtraBars.ItemClickEventArgs e)
        {
            frmBrowseFiles browse = new frmBrowseFiles();
            browse.Show();
                
            /*EDC15P_EEPROM eeprom = new EDC15P_EEPROM();
            eeprom.LoadFile(@"D:\Prive\Audi\TDI\spare eeprom\spare eeprom.bin");
            Console.WriteLine("key: " + eeprom.Key.ToString());
            Console.WriteLine("odo: " + eeprom.Mileage.ToString());
            Console.WriteLine("vin: " + eeprom.Vin);
            Console.WriteLine("immo: " + eeprom.Immo);
            Console.WriteLine("immoact: " + eeprom.ImmoActive.ToString());*/

            /*
            EDC15PTuner tuner = new EDC15PTuner();
            tuner.TuneEDC15PFile(Tools.Instance.m_currentfile, Tools.Instance.m_symbols, 400, 170);*/

            //D:\Prive\ECU\audi\BinCollection
            //
            //ParseDirectory(@"D:\Prive\Audi");
            //ParseDirectory(@"D:\Prive\ECU\audi");
            

            /*
            if (Directory.Exists(@"D:\Prive\Audi\TDI"))
            {
                string[] files = Directory.GetFiles(@"D:\Prive\Audi\TDI", "*.bin", SearchOption.AllDirectories);
                foreach (string file in files)
                {
                    OpenFile(file);
                    bool found = false;
                    foreach (SymbolHelper sh in Tools.Instance.m_symbols)
                    {
                        if (sh.Varname.StartsWith("SVBL Boost limiter"))
                        {
                            Console.WriteLine("SVBL @" + sh.Flash_start_address.ToString("X8") + " in " + file);
                            found = true;
                        }
                    }
                    if (!found)
                    {
                        Console.WriteLine("No SVBL found in " + file);
                    }
                }
            }*/
        }

        private void ParseDirectory(string folder)
        {
            if (Directory.Exists(folder))
            {
                string[] files = Directory.GetFiles(folder, "*.*", SearchOption.AllDirectories);
                foreach (string file in files)
                {
                    FileInfo fi = new FileInfo(file);
                    IEDCFileParser parser = Tools.Instance.GetParserForFile(file, false);

                   
                    
                        OpenFile(file, false);
                        byte[] allBytes = File.ReadAllBytes(file);
                        string boschnumber = parser.ExtractBoschPartnumber(allBytes);
                        string partnumber = parser.ExtractPartnumber(allBytes);
                        string softwareNumber = parser.ExtractSoftwareNumber(allBytes);
                        partNumberConverter pnc = new partNumberConverter();
                        ECUInfo info = pnc.ConvertPartnumber(boschnumber, allBytes.Length);
                        UInt32 chks = AddChecksum(allBytes);
                        // determine peak trq&hp
                        if (info.EcuType.StartsWith("EDC15P"))
                        {
                            // export to the final folder
                            string destFile = Path.Combine(@"D:\Prive\ECU\audi\BinCollection\output", /*info.CarMake + "_" + info.EcuType + "_" +*/ boschnumber + "_" + softwareNumber + "_" + partnumber + "_" + chks.ToString("X8") + ".bin");
                            if (File.Exists(destFile)) Console.WriteLine("Double file: " + destFile);
                            else
                            {
                                File.Copy(file, destFile, false);
                            }
                        }
                    
                }
            }
            Console.WriteLine("Done");
        }

        private UInt32 AddChecksum(byte[] allBytes)
        {
            UInt32 retval = 0;
            foreach (byte b in allBytes)
            {
                retval += b;
            }
            return retval;
        }

        private void btnAppSettings_ItemClick(object sender, DevExpress.XtraBars.ItemClickEventArgs e)
        {
            frmSettings set = new frmSettings();
            set.AppSettings = m_appSettings;
            set.UseCodeBlockSynchroniser = m_appSettings.CodeBlockSyncActive;
            set.ShowTablesUpsideDown = m_appSettings.ShowTablesUpsideDown;
            set.AutoSizeNewWindows = m_appSettings.AutoSizeNewWindows;
            set.AutoSizeColumnsInViewer = m_appSettings.AutoSizeColumnsInWindows;
            set.AutoUpdateChecksum = m_appSettings.AutoChecksum;
            set.ShowAddressesInHex = m_appSettings.ShowAddressesInHex;
            set.ShowGraphsInMapViewer = m_appSettings.ShowGraphs;
            set.UseRedAndWhiteMaps = m_appSettings.ShowRedWhite;
            set.ViewTablesInHex = m_appSettings.Viewinhex;
            set.AutoDockSameFile = m_appSettings.AutoDockSameFile;
            set.AutoDockSameSymbol = m_appSettings.AutoDockSameSymbol;
            set.DisableMapviewerColors = m_appSettings.DisableMapviewerColors;
            set.NewPanelsFloating = m_appSettings.NewPanelsFloating;
            set.AutoLoadLastFile = m_appSettings.AutoLoadLastFile;
            set.DefaultViewType = m_appSettings.DefaultViewType;
            set.DefaultViewSize = m_appSettings.DefaultViewSize;
            set.SynchronizeMapviewers = m_appSettings.SynchronizeMapviewers;
            set.SynchronizeMapviewersDifferentMaps = m_appSettings.SynchronizeMapviewersDifferentMaps;

            set.ProjectFolder = m_appSettings.ProjectFolder;
            set.RequestProjectNotes = m_appSettings.RequestProjectNotes;


            if (set.ShowDialog() == DialogResult.OK)
            {
                m_appSettings.ShowTablesUpsideDown = set.ShowTablesUpsideDown;
                m_appSettings.CodeBlockSyncActive = set.UseCodeBlockSynchroniser;
                m_appSettings.AutoSizeNewWindows = set.AutoSizeNewWindows;
                m_appSettings.AutoSizeColumnsInWindows = set.AutoSizeColumnsInViewer;
                m_appSettings.AutoChecksum = set.AutoUpdateChecksum;
                m_appSettings.ShowAddressesInHex = set.ShowAddressesInHex;
                m_appSettings.ShowGraphs = set.ShowGraphsInMapViewer;
                m_appSettings.ShowRedWhite = set.UseRedAndWhiteMaps;
                m_appSettings.Viewinhex = set.ViewTablesInHex;
                m_appSettings.DisableMapviewerColors = set.DisableMapviewerColors;
                m_appSettings.AutoDockSameFile = set.AutoDockSameFile;
                m_appSettings.AutoDockSameSymbol = set.AutoDockSameSymbol;
                m_appSettings.NewPanelsFloating = set.NewPanelsFloating;
                m_appSettings.DefaultViewType = set.DefaultViewType;
                m_appSettings.DefaultViewSize = set.DefaultViewSize;
                m_appSettings.AutoLoadLastFile = set.AutoLoadLastFile;
                m_appSettings.SynchronizeMapviewers = set.SynchronizeMapviewers;
                m_appSettings.SynchronizeMapviewersDifferentMaps = set.SynchronizeMapviewersDifferentMaps;

                m_appSettings.ProjectFolder = set.ProjectFolder;
                m_appSettings.RequestProjectNotes = set.RequestProjectNotes;

            }
            SetFilterMode();
        }

        private void SetFilterMode()
        {
            if (m_appSettings.ShowAddressesInHex)
            {
                gcSymbolAddress.DisplayFormat.FormatType = DevExpress.Utils.FormatType.Numeric;
                gcSymbolAddress.DisplayFormat.FormatString = "X6";
                gcSymbolAddress.FilterMode = DevExpress.XtraGrid.ColumnFilterMode.DisplayText;
                gcSymbolLength.DisplayFormat.FormatType = DevExpress.Utils.FormatType.Numeric;
                gcSymbolLength.DisplayFormat.FormatString = "X6";
                gcSymbolLength.FilterMode = DevExpress.XtraGrid.ColumnFilterMode.DisplayText;
                gcSymbolXID.DisplayFormat.FormatType = DevExpress.Utils.FormatType.Numeric;
                gcSymbolXID.DisplayFormat.FormatString = "X4";
                gcSymbolXID.FilterMode = DevExpress.XtraGrid.ColumnFilterMode.DisplayText;
                gcSymbolYID.DisplayFormat.FormatType = DevExpress.Utils.FormatType.Numeric;
                gcSymbolYID.DisplayFormat.FormatString = "X4";
                gcSymbolYID.FilterMode = DevExpress.XtraGrid.ColumnFilterMode.DisplayText;
            }
            else
            {
                gcSymbolAddress.DisplayFormat.FormatType = DevExpress.Utils.FormatType.Numeric;
                gcSymbolAddress.DisplayFormat.FormatString = "";
                gcSymbolAddress.FilterMode = DevExpress.XtraGrid.ColumnFilterMode.Value;
                gcSymbolLength.DisplayFormat.FormatType = DevExpress.Utils.FormatType.Numeric;
                gcSymbolLength.DisplayFormat.FormatString = "";
                gcSymbolLength.FilterMode = DevExpress.XtraGrid.ColumnFilterMode.Value;
                gcSymbolXID.DisplayFormat.FormatType = DevExpress.Utils.FormatType.Numeric;
                gcSymbolXID.DisplayFormat.FormatString = "";
                gcSymbolXID.FilterMode = DevExpress.XtraGrid.ColumnFilterMode.Value;
                gcSymbolYID.DisplayFormat.FormatType = DevExpress.Utils.FormatType.Numeric;
                gcSymbolYID.DisplayFormat.FormatString = "";
                gcSymbolYID.FilterMode = DevExpress.XtraGrid.ColumnFilterMode.Value;
            }
        }

        void InitSkins()
        {
            ribbonControl1.ForceInitialize();
            //barManager1.ForceInitialize();
            BarButtonItem item;

            DevExpress.Skins.SkinManager.Default.RegisterAssembly(typeof(DevExpress.UserSkins.BonusSkins).Assembly);
            DevExpress.Skins.SkinManager.Default.RegisterAssembly(typeof(DevExpress.UserSkins.OfficeSkins).Assembly);

            foreach (DevExpress.Skins.SkinContainer cnt in DevExpress.Skins.SkinManager.Default.Skins)
            {
                item = new BarButtonItem();
                item.Caption = cnt.SkinName;
                //iPaintStyle.AddItem(item);
                rbnPageGroupSkins.ItemLinks.Add(item);
                item.ItemClick += new ItemClickEventHandler(OnSkinClick);
            }

            try
            {
                DevExpress.LookAndFeel.UserLookAndFeel.Default.SetSkinStyle(m_appSettings.Skinname);
            }
            catch (Exception E)
            {
                Console.WriteLine(E.Message);
            }
            SetToolstripTheme();
        }

        private void SetToolstripTheme()
        {
            //Console.WriteLine("Rendermode was: " + ToolStripManager.RenderMode.ToString());
            //Console.WriteLine("Visual styles: " + ToolStripManager.VisualStylesEnabled.ToString());
            //Console.WriteLine("Skinname: " + appSettings.SkinName);
            //Console.WriteLine("Backcolor: " + defaultLookAndFeel1.LookAndFeel.Painter.Button.DefaultAppearance.BackColor.ToString());
            //Console.WriteLine("Backcolor2: " + defaultLookAndFeel1.LookAndFeel.Painter.Button.DefaultAppearance.BackColor2.ToString());
            try
            {
                Skin currentSkin = CommonSkins.GetSkin(defaultLookAndFeel1.LookAndFeel);
                Color c = currentSkin.TranslateColor(SystemColors.Control);
                ToolStripManager.RenderMode = ToolStripManagerRenderMode.Professional;
                ProfColorTable profcolortable = new ProfColorTable();
                profcolortable.CustomToolstripGradientBegin = c;
                profcolortable.CustomToolstripGradientMiddle = c;
                profcolortable.CustomToolstripGradientEnd = c;
                ToolStripManager.Renderer = new ToolStripProfessionalRenderer(profcolortable);
            }
            catch (Exception)
            {

            }

        }

        /// <summary>
        /// OnSkinClick: Als er een skin gekozen wordt door de gebruiker voer deze
        /// dan door in de user interface
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        void OnSkinClick(object sender, DevExpress.XtraBars.ItemClickEventArgs e)
        {
            string skinName = e.Item.Caption;
            DevExpress.LookAndFeel.UserLookAndFeel.Default.SetSkinStyle(skinName);
            m_appSettings.Skinname = skinName;
            SetToolstripTheme();
        }

        private string GetAppDataPath()
        {
            return Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        }

        private void LoadLayoutFiles()
        {
            try
            {
                if (File.Exists(Path.Combine(GetAppDataPath(), "SymbolViewLayout.xml")))
                {
                    gridViewSymbols.RestoreLayoutFromXml(Path.Combine(GetAppDataPath(), "SymbolViewLayout.xml"));
                }
                if(m_appSettings.SymbolDockWidth > 2)
                {
                    dockSymbols.Width = m_appSettings.SymbolDockWidth;
                }
            }
            catch (Exception E1)
            {
                Console.WriteLine(E1.Message);
            }
        }

        private void SaveLayoutFiles()
        {
            try
            {
                m_appSettings.SymbolDockWidth = dockSymbols.Width;
                gridViewSymbols.SaveLayoutToXml(Path.Combine(GetAppDataPath(), "SymbolViewLayout.xml"));

            }
            catch (Exception E)
            {
                Console.WriteLine(E.Message);
            }
        }

        private void frmMain_Load(object sender, EventArgs e)
        {
            try
            {
                m_appSettings = new AppSettings();
            }
            catch (Exception)
            {

            }
            InitSkins();
            LoadLayoutFiles();
            
            if (m_appSettings.DebugMode)
            {
                btnTestFiles.Visibility = DevExpress.XtraBars.BarItemVisibility.Always;
            }
            else
            {
                btnTestFiles.Visibility = DevExpress.XtraBars.BarItemVisibility.Never;
            }
        }

        private void btnCheckForUpdates_ItemClick(object sender, ItemClickEventArgs e)
        {
            try
            {
                if (m_msiUpdater != null)
                {
                    m_msiUpdater.CheckForUpdates("Global", "http://trionic.mobixs.eu/vagedcsuite/", "", "", false);
                }
            }
            catch (Exception E)
            {
                Console.WriteLine(E.Message);
            }
        }

        private void frmMain_Shown(object sender, EventArgs e)
        {
            try
            {
                if (splash != null)
                    splash.Hide();
            }
            catch (Exception)
            {

            }
            try
            {
                if (m_appSettings.AutoLoadLastFile)
                {
                    if (m_appSettings.LastOpenedType == 0)
                    {
                        if (m_appSettings.Lastfilename != "")
                        {
                            if (File.Exists(m_appSettings.Lastfilename))
                            {
                                OpenFile(m_appSettings.Lastfilename, false);
                            }
                        }
                    }
                    else if (m_appSettings.Lastprojectname != "")
                    {
                        OpenProject(m_appSettings.Lastprojectname);
                    }
                }
                SetFilterMode();
            }
            catch (Exception)
            {

            }

            try
            {
                m_msiUpdater = new msiupdater(new Version(System.Windows.Forms.Application.ProductVersion));
                m_msiUpdater.Apppath = System.Windows.Forms.Application.UserAppDataPath;
                m_msiUpdater.onDataPump += new msiupdater.DataPump(m_msiUpdater_onDataPump);
                m_msiUpdater.onUpdateProgressChanged += new msiupdater.UpdateProgressChanged(m_msiUpdater_onUpdateProgressChanged);
                m_msiUpdater.CheckForUpdates("Global", "http://trionic.mobixs.eu/vagedcsuite/", "", "", false);
            }
            catch (Exception E)
            {
                Console.WriteLine(E.Message);
            }
        }

        void m_msiUpdater_onUpdateProgressChanged(msiupdater.MSIUpdateProgressEventArgs e)
        {

        }


        private void SetStatusText(string text)
        {
            barUpdateText.Caption = text;
            System.Windows.Forms.Application.DoEvents();
        }

        void m_msiUpdater_onDataPump(msiupdater.MSIUpdaterEventArgs e)
        {
            SetStatusText(e.Data);
            if (e.UpdateAvailable)
            {

                if (e.XMLFile != "" && e.Version.ToString() != "0.0")
                {
                    if (!this.IsDisposed)
                    {
                        try
                        {
                            this.Invoke(m_DelegateStartReleaseNotePanel, e.XMLFile, e.Version.ToString());
                        }
                        catch (Exception E)
                        {
                            Console.WriteLine(E.Message);
                        }
                    }
                }

                //this.Invoke(m_DelegateShowChangeLog, e.Version);
                frmUpdateAvailable frmUpdate = new frmUpdateAvailable();
                frmUpdate.SetVersionNumber(e.Version.ToString());
                if (m_msiUpdater != null)
                {
                    m_msiUpdater.Blockauto_updates = false;
                }
                if (frmUpdate.ShowDialog() == DialogResult.OK)
                {
                    if (m_msiUpdater != null)
                    {
                        m_msiUpdater.ExecuteUpdate(e.Version);
                        System.Windows.Forms.Application.Exit();
                    }
                }
                else
                {
                    // user chose "NO", don't bug him again!
                    if (m_msiUpdater != null)
                    {
                        m_msiUpdater.Blockauto_updates = false;
                    }
                }
            }
        }

        private void btnReleaseNotes_ItemClick(object sender, ItemClickEventArgs e)
        {
            StartReleaseNotesViewer(m_msiUpdater.GetReleaseNotes(), System.Windows.Forms.Application.ProductVersion.ToString());
        }

        private void btnAbout_ItemClick(object sender, ItemClickEventArgs e)
        {
            frmAbout about = new frmAbout();
            about.SetInformation("VAGEDCSuite v" + System.Windows.Forms.Application.ProductVersion.ToString());
            about.ShowDialog();
        }

        private void btnViewFileInHex_ItemClick(object sender, ItemClickEventArgs e)
        {
            StartHexViewer();
        }

        private void StartHexViewer()
        {
            if (Tools.Instance.m_currentfile != "")
            {
                dockManager1.BeginUpdate();
                try
                {
                    DevExpress.XtraBars.Docking.DockPanel dockPanel;
                    //= dockManager1.AddPanel(DevExpress.XtraBars.Docking.DockingStyle.Right);
                    if (!m_appSettings.NewPanelsFloating)
                    {
                        dockPanel = dockManager1.AddPanel(DevExpress.XtraBars.Docking.DockingStyle.Right);
                    }
                    else
                    {
                        System.Drawing.Point floatpoint = this.PointToClient(new System.Drawing.Point(dockSymbols.Location.X + dockSymbols.Width + 30, dockSymbols.Location.Y + 10));
                        dockPanel = dockManager1.AddPanel(floatpoint);
                    }

                    dockPanel.Text = "Hexviewer: " + Path.GetFileName(Tools.Instance.m_currentfile);
                    HexViewer hv = new HexViewer();
                    hv.Issramviewer = false;
                    hv.Dock = DockStyle.Fill;
                    dockPanel.Width = 580;
                    hv.LoadDataFromFile(Tools.Instance.m_currentfile, Tools.Instance.m_symbols);
                    dockPanel.ClosedPanel += new DevExpress.XtraBars.Docking.DockPanelEventHandler(dockPanel_ClosedPanel);
                    dockPanel.Controls.Add(hv);
                }
                catch (Exception E)
                {
                    Console.WriteLine(E.Message);
                }
                dockManager1.EndUpdate();
            }
        }

        private bool ValidateFile()
        {
            bool retval = true;
            if (File.Exists(Tools.Instance.m_currentfile))
            {
                if (Tools.Instance.m_currentfile == string.Empty)
                {
                    retval = false;
                }
                else
                {
                    FileInfo fi = new FileInfo(Tools.Instance.m_currentfile);
                    if (fi.Length != 0x80000)
                    {
                        retval = false;
                    }
                }
            }
            else
            {
                retval = false;
                Tools.Instance.m_currentfile = string.Empty;
            }
            return retval;
        }

        private void btnSearchMaps_ItemClick(object sender, ItemClickEventArgs e)
        {
            // ask the user for which value to search and if searching should include symbolnames and/or symbol description
            if (ValidateFile())
            {
                SymbolCollection result_Collection = new SymbolCollection();
                frmSearchMaps searchoptions = new frmSearchMaps();
                if (searchoptions.ShowDialog() == DialogResult.OK)
                {
                    frmProgress progress = new frmProgress();
                    progress.SetProgress("Start searching data...");
                    progress.SetProgressPercentage(0);
                    progress.Show();
                    System.Windows.Forms.Application.DoEvents();
                    int cnt = 0;
                    foreach (SymbolHelper sh in Tools.Instance.m_symbols)
                    {
                        progress.SetProgress("Searching " + sh.Varname);
                        progress.SetProgressPercentage((cnt * 100) / Tools.Instance.m_symbols.Count);
                        bool hit_found = false;
                        if (searchoptions.IncludeSymbolNames)
                        {
                            if (searchoptions.SearchForNumericValues)
                            {
                                if (sh.Varname.Contains(searchoptions.NumericValueToSearchFor.ToString()))
                                {
                                    hit_found = true;
                                }
                            }
                            if (searchoptions.SearchForStringValues)
                            {
                                if (searchoptions.StringValueToSearchFor != string.Empty)
                                {
                                    if (sh.Varname.Contains(searchoptions.StringValueToSearchFor))
                                    {
                                        hit_found = true;
                                    }
                                }
                            }
                        }
                        if (searchoptions.IncludeSymbolDescription)
                        {
                            if (searchoptions.SearchForNumericValues)
                            {
                                if (sh.Description.Contains(searchoptions.NumericValueToSearchFor.ToString()))
                                {
                                    hit_found = true;
                                }
                            }
                            if (searchoptions.SearchForStringValues)
                            {
                                if (searchoptions.StringValueToSearchFor != string.Empty)
                                {
                                    if (sh.Description.Contains(searchoptions.StringValueToSearchFor))
                                    {
                                        hit_found = true;
                                    }
                                }
                            }
                        }
                        // now search the symbol data
                        if (sh.Flash_start_address < Tools.Instance.m_currentfilelength)
                        {
                            byte[] symboldata = Tools.Instance.readdatafromfile(Tools.Instance.m_currentfile, (int)sh.Flash_start_address, sh.Length, Tools.Instance.m_currentFileType);
                            if (searchoptions.SearchForNumericValues)
                            {
                                for (int i = 0; i < symboldata.Length / 2; i += 2)
                                {
                                    float value = Convert.ToInt32(symboldata.GetValue(i)) * 256;
                                    value += Convert.ToInt32(symboldata.GetValue(i + 1));
                                    value *= (float)GetMapCorrectionFactor(sh.Varname);
                                    value += (float)GetMapCorrectionOffset(sh.Varname);
                                    if (value == (float)searchoptions.NumericValueToSearchFor)
                                    {
                                        hit_found = true;
                                    }
                                }
                            }
                            if (searchoptions.SearchForStringValues)
                            {
                                if (searchoptions.StringValueToSearchFor.Length > symboldata.Length)
                                {
                                    // possible...
                                    string symboldataasstring = System.Text.Encoding.ASCII.GetString(symboldata);
                                    if (symboldataasstring.Contains(searchoptions.StringValueToSearchFor))
                                    {
                                        hit_found = true;
                                    }
                                }
                            }
                        }

                        if (hit_found)
                        {
                            // add to collection
                            result_Collection.Add(sh);
                        }
                        cnt++;
                    }
                    progress.Close();
                    if (result_Collection.Count == 0)
                    {
                        frmInfoBox info = new frmInfoBox("No results found...");
                    }
                    else
                    {
                        // start result screen
                        dockManager1.BeginUpdate();
                        try
                        {
                            SymbolTranslator st = new SymbolTranslator();
                            DevExpress.XtraBars.Docking.DockPanel dockPanel = dockManager1.AddPanel(new System.Drawing.Point(-500, -500));
                            CompareResults tabdet = new CompareResults();
                            tabdet.ShowAddressesInHex = m_appSettings.ShowAddressesInHex;
                            tabdet.SetFilterMode(m_appSettings.ShowAddressesInHex);
                            tabdet.Dock = DockStyle.Fill;
                            tabdet.UseForFind = true;
                            tabdet.Filename = Tools.Instance.m_currentfile;
                            tabdet.onSymbolSelect += new CompareResults.NotifySelectSymbol(tabdet_onSymbolSelectForFind);
                            dockPanel.Controls.Add(tabdet);
                            dockPanel.Text = "Search results: " + Path.GetFileName(Tools.Instance.m_currentfile);
                            dockPanel.DockTo(dockManager1, DevExpress.XtraBars.Docking.DockingStyle.Left, 1);

                            dockPanel.Width = 500;

                            System.Data.DataTable dt = new System.Data.DataTable();
                            dt.Columns.Add("SYMBOLNAME");
                            dt.Columns.Add("SRAMADDRESS", Type.GetType("System.Int32"));
                            dt.Columns.Add("FLASHADDRESS", Type.GetType("System.Int32"));
                            dt.Columns.Add("LENGTHBYTES", Type.GetType("System.Int32"));
                            dt.Columns.Add("LENGTHVALUES", Type.GetType("System.Int32"));
                            dt.Columns.Add("DESCRIPTION");
                            dt.Columns.Add("ISCHANGED", Type.GetType("System.Boolean"));
                            dt.Columns.Add("CATEGORY"); //0
                            dt.Columns.Add("DIFFPERCENTAGE", Type.GetType("System.Double"));
                            dt.Columns.Add("DIFFABSOLUTE", Type.GetType("System.Int32"));
                            dt.Columns.Add("DIFFAVERAGE", Type.GetType("System.Double"));
                            dt.Columns.Add("CATEGORYNAME");
                            dt.Columns.Add("SUBCATEGORYNAME");
                            dt.Columns.Add("SymbolNumber1", Type.GetType("System.Int32"));
                            dt.Columns.Add("SymbolNumber2", Type.GetType("System.Int32"));
                            dt.Columns.Add("CodeBlock1", Type.GetType("System.Int32"));
                            dt.Columns.Add("CodeBlock2", Type.GetType("System.Int32"));

                            string ht = string.Empty;
                            XDFCategories cat = XDFCategories.Undocumented;
                            XDFSubCategory subcat = XDFSubCategory.Undocumented;
                            foreach (SymbolHelper shfound in result_Collection)
                            {
                                string helptext = st.TranslateSymbolToHelpText(shfound.Varname);
                                if (shfound.Varname.Contains("."))
                                {
                                    try
                                    {
                                        shfound.Category = shfound.Varname.Substring(0, shfound.Varname.IndexOf("."));
                                    }
                                    catch (Exception cE)
                                    {
                                        Console.WriteLine("Failed to assign category to symbol: " + shfound.Varname + " err: " + cE.Message);
                                    }
                                }
                                dt.Rows.Add(shfound.Varname, shfound.Start_address, shfound.Flash_start_address, shfound.Length, shfound.Length, helptext, false, 0, 0, 0, 0, shfound.Category, "", shfound.Symbol_number, shfound.Symbol_number, shfound.CodeBlock, shfound.CodeBlock);
                            }
                            tabdet.CompareSymbolCollection = result_Collection;
                            tabdet.OpenGridViewGroups(tabdet.gridControl1, 1);
                            tabdet.gridControl1.DataSource = dt.Copy();

                        }
                        catch (Exception E)
                        {
                            Console.WriteLine(E.Message);
                        }
                        dockManager1.EndUpdate();

                    }


                }
            }
        }

        void tabdet_onSymbolSelectForFind(object sender, CompareResults.SelectSymbolEventArgs e)
        {
            StartTableViewer(e.SymbolName, e.CodeBlock1);
        }

        private void btnSaveAs_ItemClick(object sender, ItemClickEventArgs e)
        {
            SaveFileDialog sfd = new SaveFileDialog();
            sfd.Filter = "Binary files|*.bin";
            sfd.Title = "Save current file as... ";
            sfd.CheckFileExists = false;
            sfd.CheckPathExists = true;

            if (sfd.ShowDialog() == DialogResult.OK)
            {
                // Sauvegarder les cartes créées avant de copier le fichier
                SaveCreatedMaps(Tools.Instance.m_currentfile);

                // Copier le fichier principal
                File.Copy(Tools.Instance.m_currentfile, sfd.FileName, true);

                // Copier également le fichier de métadonnées s'il existe
                string sourceMetadataPath = Path.Combine(
                    Path.GetDirectoryName(Tools.Instance.m_currentfile),
                    Path.GetFileNameWithoutExtension(Tools.Instance.m_currentfile) + "createdmaps"
                );

                string targetMetadataPath = Path.Combine(
                    Path.GetDirectoryName(sfd.FileName),
                    Path.GetFileNameWithoutExtension(sfd.FileName) + ".createdmaps"
                );

                if (File.Exists(sourceMetadataPath))
                {
                    File.Copy(sourceMetadataPath, targetMetadataPath, true);
                }

                if (MessageBox.Show("Do you want to open the newly saved file?", "Question", MessageBoxButtons.YesNo) == DialogResult.Yes)
                {
                    m_appSettings.Lastprojectname = "";
                    CloseProject();
                    OpenFile(sfd.FileName, true);
                    m_appSettings.LastOpenedType = 0;
                }
            }
        }

        private void CloseProject()
        {
            Tools.Instance.m_CurrentWorkingProject = string.Empty;
            Tools.Instance.m_currentfile = string.Empty;
            gridControl1.DataSource = null;
            barFilenameText.Caption = "No file";
            m_appSettings.Lastfilename = string.Empty;

            btnCloseProject.Enabled = false;
            btnShowProjectLogbook.Enabled = false;
            btnProduceLatestBinary.Enabled = false;
            btnAddNoteToProject.Enabled = false;
            btnEditProject.Enabled = false;
            btnRebuildFile.Enabled = false;
            btnRollback.Enabled = false;
            btnRollforward.Enabled = false;
            btnShowTransactionLog.Enabled = false;

            this.Text = "VAGEDCSuite";
        }


        private void OpenProject(string projectname)
        {
            //TODO: Are there pending changes in the optionally currently opened binary file / project?
            if (Directory.Exists(m_appSettings.ProjectFolder + "\\" + projectname))
            {
                m_appSettings.LastOpenedType = 1;
                Tools.Instance.m_CurrentWorkingProject = projectname;
                Tools.Instance.m_ProjectLog.OpenProjectLog(m_appSettings.ProjectFolder + "\\" + projectname);
                //Load the binary file that comes with this project
                LoadBinaryForProject(projectname);
                //LoadAFRMapsForProject(projectname); // <GS-27072010> TODO: nog bekijken voor T7
                if (Tools.Instance.m_currentfile != string.Empty)
                {
                    // transaction log <GS-15032010>

                    Tools.Instance.m_ProjectTransactionLog = new TransactionLog();
                    if (Tools.Instance.m_ProjectTransactionLog.OpenTransActionLog(m_appSettings.ProjectFolder, projectname))
                    {
                        Tools.Instance.m_ProjectTransactionLog.ReadTransactionFile();
                        if (Tools.Instance.m_ProjectTransactionLog.TransCollection.Count > 2000)
                        {
                            frmProjectTransactionPurge frmPurge = new frmProjectTransactionPurge();
                            frmPurge.SetNumberOfTransactions(Tools.Instance.m_ProjectTransactionLog.TransCollection.Count);
                            if (frmPurge.ShowDialog() == DialogResult.OK)
                            {
                                Tools.Instance.m_ProjectTransactionLog.Purge();
                            }
                        }
                    }
                    // transaction log <GS-15032010>
                    btnCloseProject.Enabled = true;
                    btnAddNoteToProject.Enabled = true;
                    btnEditProject.Enabled = true;
                    btnShowProjectLogbook.Enabled = true;
                    btnProduceLatestBinary.Enabled = true;
                    //btncreateb                    
                    btnRebuildFile.Enabled = true;
                    CreateProjectBackupFile();
                    UpdateRollbackForwardControls();
                    m_appSettings.Lastprojectname = Tools.Instance.m_CurrentWorkingProject;
                    this.Text = "VAGEDCSuite [Project: " + projectname + "]";
                }
            }
        }

        private void UpdateRollbackForwardControls()
        {
            btnRollback.Enabled = false;
            btnRollforward.Enabled = false;
            btnShowTransactionLog.Enabled = false;
            if (Tools.Instance.m_ProjectTransactionLog != null)
            {
                for (int t = Tools.Instance.m_ProjectTransactionLog.TransCollection.Count - 1; t >= 0; t--)
                {
                    if (!btnShowTransactionLog.Enabled) btnShowTransactionLog.Enabled = true;
                    if (Tools.Instance.m_ProjectTransactionLog.TransCollection[t].IsRolledBack)
                    {
                        btnRollforward.Enabled = true;
                    }
                    else
                    {
                        btnRollback.Enabled = true;
                    }
                }
            }
        }

        private void CreateProjectBackupFile()
        {
            // create a backup file automatically! <GS-16032010>
            if (!Directory.Exists(m_appSettings.ProjectFolder + "\\" + Tools.Instance.m_CurrentWorkingProject + "\\Backups")) Directory.CreateDirectory(m_appSettings.ProjectFolder + "\\" + Tools.Instance.m_CurrentWorkingProject + "\\Backups");
            string filename = m_appSettings.ProjectFolder + "\\" + Tools.Instance.m_CurrentWorkingProject + "\\Backups\\" + Path.GetFileNameWithoutExtension(GetBinaryForProject(Tools.Instance.m_CurrentWorkingProject)) + "-backup-" + DateTime.Now.ToString("MMddyyyyHHmmss") + ".BIN";
            File.Copy(GetBinaryForProject(Tools.Instance.m_CurrentWorkingProject), filename);
            if (Tools.Instance.m_CurrentWorkingProject != string.Empty)
            {
                Tools.Instance.m_ProjectLog.WriteLogbookEntry(LogbookEntryType.BackupfileCreated, filename);
            }


        }


        private void LoadBinaryForProject(string projectname)
        {
            if (File.Exists(m_appSettings.ProjectFolder + "\\" + projectname + "\\projectproperties.xml"))
            {
                System.Data.DataTable projectprops = new System.Data.DataTable("T5PROJECT");
                projectprops.Columns.Add("CARMAKE");
                projectprops.Columns.Add("CARMODEL");
                projectprops.Columns.Add("CARMY");
                projectprops.Columns.Add("CARVIN");
                projectprops.Columns.Add("NAME");
                projectprops.Columns.Add("BINFILE");
                projectprops.Columns.Add("VERSION");
                projectprops.ReadXml(m_appSettings.ProjectFolder + "\\" + projectname + "\\projectproperties.xml");
                // valid project, add it to the list
                if (projectprops.Rows.Count > 0)
                {
                    OpenFile(projectprops.Rows[0]["BINFILE"].ToString(), true);
                }
            }
        }

        private string GetBinaryForProject(string projectname)
        {
            string retval = Tools.Instance.m_currentfile;
            if (File.Exists(m_appSettings.ProjectFolder + "\\" + projectname + "\\projectproperties.xml"))
            {
                System.Data.DataTable projectprops = new System.Data.DataTable("T5PROJECT");
                projectprops.Columns.Add("CARMAKE");
                projectprops.Columns.Add("CARMODEL");
                projectprops.Columns.Add("CARMY");
                projectprops.Columns.Add("CARVIN");
                projectprops.Columns.Add("NAME");
                projectprops.Columns.Add("BINFILE");
                projectprops.Columns.Add("VERSION");
                projectprops.ReadXml(m_appSettings.ProjectFolder + "\\" + projectname + "\\projectproperties.xml");
                // valid project, add it to the list
                if (projectprops.Rows.Count > 0)
                {
                    retval = projectprops.Rows[0]["BINFILE"].ToString();
                }
            }
            return retval;
        }

        private string GetBackupOlderThanDateTime(string project, DateTime mileDT)
        {
            string retval = Tools.Instance.m_currentfile; // default = current file
            string BackupPath = m_appSettings.ProjectFolder + "\\" + project + "\\Backups";
            DateTime MaxDateTime = DateTime.MinValue;
            string foundBackupfile = string.Empty;
            if (Directory.Exists(BackupPath))
            {
                string[] backupfiles = Directory.GetFiles(BackupPath, "*.bin");
                foreach (string backupfile in backupfiles)
                {
                    FileInfo fi = new FileInfo(backupfile);
                    if (fi.LastAccessTime > MaxDateTime && fi.LastAccessTime <= mileDT)
                    {
                        MaxDateTime = fi.LastAccessTime;
                        foundBackupfile = backupfile;
                    }
                }
            }
            if (foundBackupfile != string.Empty)
            {
                retval = foundBackupfile;
            }
            return retval;
        }

        private void btnRebuildFile_ItemClick(object sender, ItemClickEventArgs e)
        {
            // show the transactionlog again and ask the user upto what datetime he wants to rebuild the file
            // first ask a datetime
            frmRebuildFileParameters filepar = new frmRebuildFileParameters();
            if (filepar.ShowDialog() == DialogResult.OK)
            {

                // get the last backup that is older than the selected datetime
                string file2Process = GetBackupOlderThanDateTime(Tools.Instance.m_CurrentWorkingProject, filepar.SelectedDateTime);
                // now rebuild the file
                // first create a copy of this file
                string tempRebuildFile = m_appSettings.ProjectFolder + "\\" + Tools.Instance.m_CurrentWorkingProject + "rebuild.bin";
                if (File.Exists(tempRebuildFile))
                {
                    File.Delete(tempRebuildFile);
                }
                // CREATE A BACKUP FILE HERE
                CreateProjectBackupFile();
                File.Copy(file2Process, tempRebuildFile);
                // now do all the transactions newer than this file and older than the selected date time
                FileInfo fi = new FileInfo(file2Process);
                foreach (TransactionEntry te in Tools.Instance.m_ProjectTransactionLog.TransCollection)
                {
                    if (te.EntryDateTime >= fi.LastAccessTime && te.EntryDateTime <= filepar.SelectedDateTime)
                    {
                        // apply this change
                        RollForwardOnFile(tempRebuildFile, te);
                    }
                }
                // rename/copy file
                if (filepar.UseAsNewProjectFile)
                {
                    // just delete the current file
                    File.Delete(Tools.Instance.m_currentfile);
                    File.Copy(tempRebuildFile, Tools.Instance.m_currentfile);
                    File.Delete(tempRebuildFile);
                    // done
                }
                else
                {
                    // ask for destination file
                    SaveFileDialog sfd = new SaveFileDialog();
                    sfd.Title = "Save rebuild file as...";
                    sfd.Filter = "Binary files|*.bin";
                    if (sfd.ShowDialog() == DialogResult.OK)
                    {
                        if (File.Exists(sfd.FileName)) File.Delete(sfd.FileName);
                        File.Copy(tempRebuildFile, sfd.FileName);
                        File.Delete(tempRebuildFile);
                    }
                }
                if (Tools.Instance.m_CurrentWorkingProject != string.Empty)
                {
                    Tools.Instance.m_ProjectLog.WriteLogbookEntry(LogbookEntryType.ProjectFileRecreated, "Reconstruct upto " + filepar.SelectedDateTime.ToString("dd/MM/yyyy") + " selected file " + file2Process);
                }
                UpdateRollbackForwardControls();
            }
        }

        private void RollForwardOnFile(string file2Rollback, TransactionEntry entry)
        {
            FileInfo fi = new FileInfo(file2Rollback);
            int addressToWrite = entry.SymbolAddress;
            while (addressToWrite > fi.Length) addressToWrite -= (int)fi.Length;
            Tools.Instance.savedatatobinary(addressToWrite, entry.SymbolLength, entry.DataAfter, file2Rollback, false, Tools.Instance.m_currentFileType);
            VerifyChecksum(Tools.Instance.m_currentfile, false, false);
        }

        private string MakeDirName(string dirname)
        {
            string retval = dirname;
            retval = retval.Replace(@"\", "");
            retval = retval.Replace(@"/", "");
            retval = retval.Replace(@":", "");
            retval = retval.Replace(@"*", "");
            retval = retval.Replace(@"?", "");
            retval = retval.Replace(@">", "");
            retval = retval.Replace(@"<", "");
            retval = retval.Replace(@"|", "");
            return retval;
        }

        private void btnCreateAProject_ItemClick(object sender, ItemClickEventArgs e)
        {
            // show the project properties screen for the user to fill in
            // if a bin file is loaded, ask the user whether this should be the new projects binary file
            // the project XML should contain a reference to this binfile as well as a lot of other stuff
            frmProjectProperties projectprops = new frmProjectProperties();
            if (Tools.Instance.m_currentfile != string.Empty)
            {
                projectprops.BinaryFile = Tools.Instance.m_currentfile;
                projectprops.CarModel = barPartnumber.Caption;// fileheader.getCarDescription().Trim();

                projectprops.ProjectName = DateTime.Now.ToString("yyyyMMdd") + "_" + barAdditionalInfo.Caption;// fileheader.getPartNumber().Trim() + " " + fileheader.getSoftwareVersion().Trim();
            }
            if (projectprops.ShowDialog() == DialogResult.OK)
            {
                if (!Directory.Exists(m_appSettings.ProjectFolder)) Directory.CreateDirectory(m_appSettings.ProjectFolder);
                // create a new folder with these project properties.
                // also copy the binary file into the subfolder for this project
                if (Directory.Exists(m_appSettings.ProjectFolder + "\\" + MakeDirName(projectprops.ProjectName)))
                {
                    frmInfoBox info = new frmInfoBox("The chosen projectname already exists, please choose another one");
                }
                else
                {
                    // create the project
                    Directory.CreateDirectory(m_appSettings.ProjectFolder + "\\" + MakeDirName(projectprops.ProjectName));
                    // copy the selected binary file to this folder
                    string binfilename = m_appSettings.ProjectFolder + "\\" + MakeDirName(projectprops.ProjectName) + "\\" + Path.GetFileName(projectprops.BinaryFile);
                    File.Copy(projectprops.BinaryFile, binfilename);
                    // now create the projectproperties.xml in this new folder
                    System.Data.DataTable dtProps = new System.Data.DataTable("T5PROJECT");
                    dtProps.Columns.Add("CARMAKE");
                    dtProps.Columns.Add("CARMODEL");
                    dtProps.Columns.Add("CARMY");
                    dtProps.Columns.Add("CARVIN");
                    dtProps.Columns.Add("NAME");
                    dtProps.Columns.Add("BINFILE");
                    dtProps.Columns.Add("VERSION");
                    dtProps.Rows.Add(projectprops.CarMake, projectprops.CarModel, projectprops.CarMY, projectprops.CarVIN, MakeDirName(projectprops.ProjectName), binfilename, projectprops.Version);
                    dtProps.WriteXml(m_appSettings.ProjectFolder + "\\" + MakeDirName(projectprops.ProjectName) + "\\projectproperties.xml");
                    OpenProject(projectprops.ProjectName); //?
                }
            }
        }

        private void btnOpenProject_ItemClick(object sender, ItemClickEventArgs e)
        {
            //let the user select a project from the Project folder. If none are present, let the user know
            if (!Directory.Exists(m_appSettings.ProjectFolder)) Directory.CreateDirectory(m_appSettings.ProjectFolder);
            System.Data.DataTable ValidProjects = new System.Data.DataTable();
            ValidProjects.Columns.Add("Projectname");
            ValidProjects.Columns.Add("NumberBackups");
            ValidProjects.Columns.Add("NumberTransactions");
            ValidProjects.Columns.Add("DateTimeModified");
            ValidProjects.Columns.Add("Version");
            string[] projects = Directory.GetDirectories(m_appSettings.ProjectFolder);
            // filter for folders with a projectproperties.xml file
            foreach (string project in projects)
            {
                string[] projectfiles = Directory.GetFiles(project, "projectproperties.xml");

                if (projectfiles.Length > 0)
                {
                    System.Data.DataTable projectprops = new System.Data.DataTable("T5PROJECT");
                    projectprops.Columns.Add("CARMAKE");
                    projectprops.Columns.Add("CARMODEL");
                    projectprops.Columns.Add("CARMY");
                    projectprops.Columns.Add("CARVIN");
                    projectprops.Columns.Add("NAME");
                    projectprops.Columns.Add("BINFILE");
                    projectprops.Columns.Add("VERSION");
                    projectprops.ReadXml((string)projectfiles.GetValue(0));
                    // valid project, add it to the list
                    if (projectprops.Rows.Count > 0)
                    {
                        string projectName = projectprops.Rows[0]["NAME"].ToString();
                        ValidProjects.Rows.Add(projectName, GetNumberOfBackups(projectName), GetNumberOfTransactions(projectName), GetLastAccessTime(projectprops.Rows[0]["BINFILE"].ToString()), projectprops.Rows[0]["VERSION"].ToString());
                    }
                }
            }
            if (ValidProjects.Rows.Count > 0)
            {
                frmProjectSelection projselection = new frmProjectSelection();
                projselection.SetDataSource(ValidProjects);
                if (projselection.ShowDialog() == DialogResult.OK)
                {
                    string selectedproject = projselection.GetProjectName();
                    if (selectedproject != "")
                    {
                        OpenProject(selectedproject);
                    }

                }
            }
            else
            {
                frmInfoBox info = new frmInfoBox("No projects were found, please create one first!");
            }
        }

        private int GetNumberOfBackups(string project)
        {
            int retval = 0;
            string dirname = m_appSettings.ProjectFolder + "\\" + project + "\\Backups";
            if (!Directory.Exists(dirname)) Directory.CreateDirectory(dirname);
            string[] backupfiles = Directory.GetFiles(dirname, "*.bin");
            retval = backupfiles.Length;
            return retval;
        }

        private int GetNumberOfTransactions(string project)
        {
            int retval = 0;
            string filename = m_appSettings.ProjectFolder + "\\" + project + "\\TransActionLogV2.ttl";
            if (File.Exists(filename))
            {
                TransactionLog translog = new TransactionLog();
                translog.OpenTransActionLog(m_appSettings.ProjectFolder, project);
                translog.ReadTransactionFile();
                retval = translog.TransCollection.Count;
            }
            return retval;
        }

        private DateTime GetLastAccessTime(string filename)
        {
            DateTime retval = DateTime.MinValue;
            if (File.Exists(filename))
            {
                FileInfo fi = new FileInfo(filename);
                retval = fi.LastAccessTime;
            }
            return retval;
        }

        private void btnCloseProject_ItemClick(object sender, ItemClickEventArgs e)
        {
            CloseProject();
            m_appSettings.Lastprojectname = "";
        }

        private void btnShowTransactionLog_ItemClick(object sender, ItemClickEventArgs e)
        {
            // show new form
            if (Tools.Instance.m_CurrentWorkingProject != string.Empty)
            {
                frmTransactionLog translog = new frmTransactionLog();
                translog.onRollBack += new frmTransactionLog.RollBack(translog_onRollBack);
                translog.onRollForward += new frmTransactionLog.RollForward(translog_onRollForward);
                translog.onNoteChanged += new frmTransactionLog.NoteChanged(translog_onNoteChanged);
                foreach (TransactionEntry entry in Tools.Instance.m_ProjectTransactionLog.TransCollection)
                {
                    entry.SymbolName = Tools.Instance.GetSymbolNameByAddress(entry.SymbolAddress);

                }
                translog.SetTransactionLog(Tools.Instance.m_ProjectTransactionLog);
                translog.Show();
            }
        }


        void translog_onNoteChanged(object sender, frmTransactionLog.RollInformationEventArgs e)
        {
            Tools.Instance.m_ProjectTransactionLog.SetEntryNote(e.Entry);
        }

        void translog_onRollForward(object sender, frmTransactionLog.RollInformationEventArgs e)
        {
            // alter the log!
            // rollback the transaction
            // now reload the list
            RollForward(e.Entry);
            if (sender is frmTransactionLog)
            {
                frmTransactionLog logfrm = (frmTransactionLog)sender;
                if (Tools.Instance.m_ProjectTransactionLog != null)
                {
                    logfrm.SetTransactionLog(Tools.Instance.m_ProjectTransactionLog);
                }
            }
        }

        private void RollForward(TransactionEntry entry)
        {
            int addressToWrite = entry.SymbolAddress;
            Tools.Instance.savedatatobinary(addressToWrite, entry.SymbolLength, entry.DataAfter, Tools.Instance.m_currentfile, false, Tools.Instance.m_currentFileType);
            VerifyChecksum(Tools.Instance.m_currentfile, false, false);
            if (Tools.Instance.m_ProjectTransactionLog != null)
            {
                Tools.Instance.m_ProjectTransactionLog.SetEntryRolledForward(entry.TransactionNumber);
            }
            if (Tools.Instance.m_CurrentWorkingProject != string.Empty)
            {

                Tools.Instance.m_ProjectLog.WriteLogbookEntry(LogbookEntryType.TransactionRolledforward, Tools.Instance.GetSymbolNameByAddress(entry.SymbolAddress) + " " + entry.Note + " " + entry.TransactionNumber.ToString());
            }

            UpdateRollbackForwardControls();
        }

        void translog_onRollBack(object sender, frmTransactionLog.RollInformationEventArgs e)
        {
            // alter the log!
            // rollback the transaction
            RollBack(e.Entry);
            // now reload the list
            if (sender is frmTransactionLog)
            {
                frmTransactionLog logfrm = (frmTransactionLog)sender;
                logfrm.SetTransactionLog(Tools.Instance.m_ProjectTransactionLog);
            }
        }


        private void RollBack(TransactionEntry entry)
        {
            int addressToWrite = entry.SymbolAddress;
            Tools.Instance.savedatatobinary(addressToWrite, entry.SymbolLength, entry.DataBefore, Tools.Instance.m_currentfile, false, Tools.Instance.m_currentFileType);
            VerifyChecksum(Tools.Instance.m_currentfile, false, false);
            if (Tools.Instance.m_ProjectTransactionLog != null)
            {
                Tools.Instance.m_ProjectTransactionLog.SetEntryRolledBack(entry.TransactionNumber);
            }
            if (Tools.Instance.m_CurrentWorkingProject != string.Empty)
            {
                Tools.Instance.m_ProjectLog.WriteLogbookEntry(LogbookEntryType.TransactionRolledback, Tools.Instance.GetSymbolNameByAddress(entry.SymbolAddress) + " " + entry.Note + " " + entry.TransactionNumber.ToString());
            }
            UpdateRollbackForwardControls();
        }

        private void btnRollback_ItemClick(object sender, ItemClickEventArgs e)
        {
            //roll back last entry in the log that has not been rolled back
            if (Tools.Instance.m_ProjectTransactionLog != null)
            {
                for (int t = Tools.Instance.m_ProjectTransactionLog.TransCollection.Count - 1; t >= 0; t--)
                {
                    if (!Tools.Instance.m_ProjectTransactionLog.TransCollection[t].IsRolledBack)
                    {
                        RollBack(Tools.Instance.m_ProjectTransactionLog.TransCollection[t]);

                        break;
                    }
                }
            }
        }

        private void btnRollforward_ItemClick(object sender, ItemClickEventArgs e)
        {
            //roll back last entry in the log that has not been rolled back
            if (Tools.Instance.m_ProjectTransactionLog != null)
            {
                for (int t = 0; t < Tools.Instance.m_ProjectTransactionLog.TransCollection.Count; t++)
                {
                    if (Tools.Instance.m_ProjectTransactionLog.TransCollection[t].IsRolledBack)
                    {
                        RollForward(Tools.Instance.m_ProjectTransactionLog.TransCollection[t]);

                        break;
                    }
                }
            }
        }

        private void btnRebuildFile_ItemClick_1(object sender, ItemClickEventArgs e)
        {
            // show the transactionlog again and ask the user upto what datetime he wants to rebuild the file
            // first ask a datetime
            frmRebuildFileParameters filepar = new frmRebuildFileParameters();
            if (filepar.ShowDialog() == DialogResult.OK)
            {

                // get the last backup that is older than the selected datetime
                string file2Process = GetBackupOlderThanDateTime(Tools.Instance.m_CurrentWorkingProject, filepar.SelectedDateTime);
                // now rebuild the file
                // first create a copy of this file
                string tempRebuildFile = m_appSettings.ProjectFolder + "\\" + Tools.Instance.m_CurrentWorkingProject + "rebuild.bin";
                if (File.Exists(tempRebuildFile))
                {
                    File.Delete(tempRebuildFile);
                }
                // CREATE A BACKUP FILE HERE
                CreateProjectBackupFile();
                File.Copy(file2Process, tempRebuildFile);
                FileInfo fi = new FileInfo(file2Process);
                foreach (TransactionEntry te in Tools.Instance.m_ProjectTransactionLog.TransCollection)
                {
                    if (te.EntryDateTime >= fi.LastAccessTime && te.EntryDateTime <= filepar.SelectedDateTime)
                    {
                        // apply this change
                        RollForwardOnFile(tempRebuildFile, te);
                    }
                }
                // rename/copy file
                if (filepar.UseAsNewProjectFile)
                {
                    // just delete the current file
                    File.Delete(Tools.Instance.m_currentfile);
                    File.Copy(tempRebuildFile, Tools.Instance.m_currentfile);
                    File.Delete(tempRebuildFile);
                    // done
                }
                else
                {
                    // ask for destination file
                    SaveFileDialog sfd = new SaveFileDialog();
                    sfd.Title = "Save rebuild file as...";
                    sfd.Filter = "Binary files|*.bin";
                    if (sfd.ShowDialog() == DialogResult.OK)
                    {
                        if (File.Exists(sfd.FileName)) File.Delete(sfd.FileName);
                        File.Copy(tempRebuildFile, sfd.FileName);
                        File.Delete(tempRebuildFile);
                    }
                }
                if (Tools.Instance.m_CurrentWorkingProject != string.Empty)
                {
                    Tools.Instance.m_ProjectLog.WriteLogbookEntry(LogbookEntryType.ProjectFileRecreated, "Reconstruct upto " + filepar.SelectedDateTime.ToString("dd/MM/yyyy") + " selected file " + file2Process);
                }
                UpdateRollbackForwardControls();
            }
        }

        private void btnEditProject_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (Tools.Instance.m_CurrentWorkingProject != string.Empty)
            {
                EditProjectProperties(Tools.Instance.m_CurrentWorkingProject);
            }
        }

        private void EditProjectProperties(string project)
        {
            // edit current project properties
            System.Data.DataTable projectprops = new System.Data.DataTable("T5PROJECT");
            projectprops.Columns.Add("CARMAKE");
            projectprops.Columns.Add("CARMODEL");
            projectprops.Columns.Add("CARMY");
            projectprops.Columns.Add("CARVIN");
            projectprops.Columns.Add("NAME");
            projectprops.Columns.Add("BINFILE");
            projectprops.Columns.Add("VERSION");
            projectprops.ReadXml(m_appSettings.ProjectFolder + "\\" + project + "\\projectproperties.xml");

            frmProjectProperties projectproperties = new frmProjectProperties();
            projectproperties.Version = projectprops.Rows[0]["VERSION"].ToString();
            projectproperties.ProjectName = projectprops.Rows[0]["NAME"].ToString();
            projectproperties.CarMake = projectprops.Rows[0]["CARMAKE"].ToString();
            projectproperties.CarModel = projectprops.Rows[0]["CARMODEL"].ToString();
            projectproperties.CarVIN = projectprops.Rows[0]["CARVIN"].ToString();
            projectproperties.CarMY = projectprops.Rows[0]["CARMY"].ToString();
            projectproperties.BinaryFile = projectprops.Rows[0]["BINFILE"].ToString();
            bool _reopenProject = false;
            if (projectproperties.ShowDialog() == DialogResult.OK)
            {
                // delete the original XML file
                if (project != projectproperties.ProjectName)
                {
                    Directory.Move(m_appSettings.ProjectFolder + "\\" + project, m_appSettings.ProjectFolder + "\\" + projectproperties.ProjectName);
                    project = projectproperties.ProjectName;
                    Tools.Instance.m_CurrentWorkingProject = project;
                    // set the working file to the correct folder
                    projectproperties.BinaryFile = Path.Combine(m_appSettings.ProjectFolder + "\\" + project, Path.GetFileName(projectprops.Rows[0]["BINFILE"].ToString()));
                    _reopenProject = true;
                    // open this project

                }

                File.Delete(m_appSettings.ProjectFolder + "\\" + project + "\\projectproperties.xml");
                System.Data.DataTable dtProps = new System.Data.DataTable("T5PROJECT");
                dtProps.Columns.Add("CARMAKE");
                dtProps.Columns.Add("CARMODEL");
                dtProps.Columns.Add("CARMY");
                dtProps.Columns.Add("CARVIN");
                dtProps.Columns.Add("NAME");
                dtProps.Columns.Add("BINFILE");
                dtProps.Columns.Add("VERSION");
                dtProps.Rows.Add(projectproperties.CarMake, projectproperties.CarModel, projectproperties.CarMY, projectproperties.CarVIN, MakeDirName(projectproperties.ProjectName), projectproperties.BinaryFile, projectproperties.Version);
                dtProps.WriteXml(m_appSettings.ProjectFolder + "\\" + MakeDirName(projectproperties.ProjectName) + "\\projectproperties.xml");
                if (_reopenProject)
                {
                    OpenProject(Tools.Instance.m_CurrentWorkingProject);
                }
                Tools.Instance.m_ProjectLog.WriteLogbookEntry(LogbookEntryType.PropertiesEdited, projectproperties.Version);

            }

        }

        private void btnAddNoteToProject_ItemClick(object sender, ItemClickEventArgs e)
        {
            frmChangeNote newNote = new frmChangeNote();
            newNote.ShowDialog();
            if (newNote.Note != string.Empty)
            {
                if (Tools.Instance.m_CurrentWorkingProject != string.Empty)
                {
                    Tools.Instance.m_ProjectLog.WriteLogbookEntry(LogbookEntryType.Note, newNote.Note);
                }
            }
        }

        private void btnShowProjectLogbook_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (Tools.Instance.m_CurrentWorkingProject != string.Empty)
            {
                frmProjectLogbook logb = new frmProjectLogbook();

                logb.LoadLogbookForProject(m_appSettings.ProjectFolder, Tools.Instance.m_CurrentWorkingProject);
                logb.Show();
            }
        }

        private void btnProduceLatestBinary_ItemClick(object sender, ItemClickEventArgs e)
        {
            // save binary as
            SaveFileDialog sfd = new SaveFileDialog();
            sfd.Filter = "Binary files|*.bin";
            if (sfd.ShowDialog() == DialogResult.OK)
            {
                // copy the current project file to the selected destination
                File.Copy(Tools.Instance.m_currentfile, sfd.FileName, true);
            }
        }

        private void frmMain_FormClosing(object sender, FormClosingEventArgs e)
        {
            SaveLayoutFiles();
            if (Tools.Instance.m_CurrentWorkingProject != "")
            {
                CloseProject();
            }
        }

        private void btnCreateBackup_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (Tools.Instance.m_currentfile != string.Empty)
            {
                VerifyChecksum(Tools.Instance.m_currentfile, false, false);

                if (File.Exists(Tools.Instance.m_currentfile))
                {
                    if (Tools.Instance.m_CurrentWorkingProject != "")
                    {
                        if (!Directory.Exists(m_appSettings.ProjectFolder + "\\" + Tools.Instance.m_CurrentWorkingProject + "\\Backups")) Directory.CreateDirectory(m_appSettings.ProjectFolder + "\\" + Tools.Instance.m_CurrentWorkingProject + "\\Backups");
                        string filename = m_appSettings.ProjectFolder + "\\" + Tools.Instance.m_CurrentWorkingProject + "\\Backups\\" + Path.GetFileNameWithoutExtension(GetBinaryForProject(Tools.Instance.m_CurrentWorkingProject)) + "-backup-" + DateTime.Now.ToString("MMddyyyyHHmmss") + ".BIN";
                        File.Copy(GetBinaryForProject(Tools.Instance.m_CurrentWorkingProject), filename);
                    }
                    else
                    {
                        File.Copy(Tools.Instance.m_currentfile, Path.GetDirectoryName(Tools.Instance.m_currentfile) + "\\" + Path.GetFileNameWithoutExtension(Tools.Instance.m_currentfile) + DateTime.Now.ToString("yyyyMMddHHmmss") + ".binarybackup", true);
                        frmInfoBox info = new frmInfoBox("Backup created: " + Path.GetDirectoryName(Tools.Instance.m_currentfile) + "\\" + Path.GetFileNameWithoutExtension(Tools.Instance.m_currentfile) + DateTime.Now.ToString("yyyyMMddHHmmss") + ".binarybackup");
                    }
                }
            }
        }

        private void btnLookupPartnumber_ItemClick(object sender, ItemClickEventArgs e)
        {
            frmPartnumberLookup lookup = new frmPartnumberLookup();
            lookup.ShowDialog();
            if (lookup.Open_File)
            {
                string filename = lookup.GetFileToOpen();
                if (filename != string.Empty)
                {
                    CloseProject();
                    m_appSettings.Lastprojectname = "";
                    OpenFile(filename, true);
                    m_appSettings.LastOpenedType = 0;

                }
            }
            else if (lookup.Compare_File)
            {
                string filename = lookup.GetFileToOpen();
                if (filename != string.Empty)
                {

                    CompareToFile(filename);
                }
            }
            else if (lookup.CreateNewFile)
            {
                string filename = lookup.GetFileToOpen();
                if (filename != string.Empty)
                {
                    CloseProject();
                    m_appSettings.Lastprojectname = "";
                    File.Copy(filename, lookup.FileNameToSave);
                    OpenFile(lookup.FileNameToSave, true);
                    m_appSettings.LastOpenedType = 0;

                }
            }
        }

        private void btnFirmwareInformation_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (Tools.Instance.m_currentfile != string.Empty)
            {
                if(File.Exists(Tools.Instance.m_currentfile))
                {

                    byte[] allBytes = File.ReadAllBytes(Tools.Instance.m_currentfile);
                    IEDCFileParser parser = Tools.Instance.GetParserForFile(Tools.Instance.m_currentfile, false);
                    partNumberConverter pnc = new partNumberConverter();
                    ECUInfo ecuinfo = pnc.ConvertPartnumber(parser.ExtractBoschPartnumber(allBytes), allBytes.Length);
                    frmFirmwareInfo info = new frmFirmwareInfo();
                    info.InfoString = parser.ExtractInfo(allBytes);
                    info.partNumber = parser.ExtractBoschPartnumber(allBytes);
                    if(ecuinfo.SoftwareID == "") ecuinfo.SoftwareID = parser.ExtractPartnumber(allBytes);
                    info.SoftwareID = ecuinfo.SoftwareID + " " + parser.ExtractSoftwareNumber(allBytes);
                    info.carDetails = ecuinfo.CarMake + " " + ecuinfo.CarType;
                    string enginedetails = ecuinfo.EngineType;
                    string hpinfo = string.Empty;
                    string tqinfo = string.Empty;
                    if (ecuinfo.HP > 0) hpinfo = ecuinfo.HP.ToString() + " bhp";
                    if (ecuinfo.TQ > 0) tqinfo = ecuinfo.TQ.ToString() + " Nm";
                    if (hpinfo != string.Empty || tqinfo != string.Empty)
                    {
                        enginedetails += " (";
                        if (hpinfo != string.Empty) enginedetails += hpinfo;
                        if (hpinfo != string.Empty && tqinfo != string.Empty) enginedetails += "/";
                        if (tqinfo != string.Empty) enginedetails += tqinfo;
                        enginedetails += ")";
                    }
                    info.EngineType = /*ecuinfo.EngineType*/ enginedetails;
                    info.ecuDetails = ecuinfo.EcuType;
                    //DumpECUInfo(ecuinfo);
                    ChecksumResultDetails result = Tools.Instance.UpdateChecksum(Tools.Instance.m_currentfile, true);
                    string chkType = string.Empty;
                    if (result.TypeResult == ChecksumType.VAG_EDC15P_V41) chkType = "VAG EDC15P V4.1";
                    else if (result.TypeResult == ChecksumType.VAG_EDC15P_V41V2) chkType = "VAG EDC15P V4.1v2";
                    else if (result.TypeResult == ChecksumType.VAG_EDC15P_V41_2002) chkType = "VAG EDC15P V4.1 2002+";
                    else if (result.TypeResult != ChecksumType.Unknown) chkType = result.TypeResult.ToString();

                    chkType += " " + result.CalculationResult.ToString();

                    info.checksumType = chkType;
                    // number of codeblocks?
                    info.codeBlocks = DetermineNumberOfCodeblocks().ToString();
                    info.ShowDialog();
                }
            }
        }

        private int DetermineNumberOfCodeblocks()
        {
            List<int> blockIds = new List<int>();
            foreach (SymbolHelper sh in Tools.Instance.m_symbols)
            {
                if (!blockIds.Contains(sh.CodeBlock) && sh.CodeBlock != 0) blockIds.Add(sh.CodeBlock);
            }
            return blockIds.Count;
        }

        private void DumpECUInfo(ECUInfo ecuinfo)
        {
            Console.WriteLine("Partnr: " + ecuinfo.PartNumber);
            Console.WriteLine("Make  : " + ecuinfo.CarMake);
            Console.WriteLine("Type  : " + ecuinfo.CarType);
            Console.WriteLine("ECU   : " + ecuinfo.EcuType);
            Console.WriteLine("Engine: " + ecuinfo.EngineType);
            Console.WriteLine("SWID  : " + ecuinfo.SoftwareID);
            Console.WriteLine("HP    : " + ecuinfo.HP.ToString());
            Console.WriteLine("TQ    : " + ecuinfo.TQ.ToString());
        }

        private void btnVINDecoder_ItemClick(object sender, ItemClickEventArgs e)
        {
            frmDecodeVIN vindec = new frmDecodeVIN();
            vindec.Show();
            //frmInfoBox info = new frmInfoBox("Not yet implemented");
        }

        private void btnChecksum_ItemClick(object sender, ItemClickEventArgs e)
        {
            
            if (Tools.Instance.m_currentfile != string.Empty)
            {
                if (File.Exists(Tools.Instance.m_currentfile))
                {
                    VerifyChecksum(Tools.Instance.m_currentfile, true, true);
                }
            }
        }


        private void btnDriverWish_ItemClick(object sender, ItemClickEventArgs e)
        {
            StartTableViewer("Driver wish", 2);

        }

        private void btnTorqueLimiter_ItemClick(object sender, ItemClickEventArgs e)
        {
            StartTableViewer("Torque limiter", 2);

           
        }
        // Vous auriez besoin d'ajouter cette fonction
        // Nouvelle méthode qui retourne un booléen indiquant si la carte a été trouvée et ouverte
        private bool OpenMapByAddress(int address)
        {
            SymbolCollection sc = (SymbolCollection)gridControl1.DataSource;
            int rtel = 0;
            foreach (SymbolHelper sh in sc)
            {
                if (sh.Flash_start_address == address)
                {
                    try
                    {
                        SélectionnerSymbole(rtel);
                        StartTableViewer();
                        return true; // La carte a été trouvée et ouverte
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("Une erreur est survenue lors de la sélection du symbole.",
                            "Erreur", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
                rtel++;
            }
            return false; // La carte n'a pas été trouvée ou ouverte
        }

        // Version modifiée qui renvoie un résultat
        private bool StartTableViewerWithResult(string symbolname, int codeblock)
        {
            // Appeler votre méthode StartTableViewer existante
            StartTableViewer(symbolname, codeblock);

            // Vérifier si une map a été trouvée (vous devrez peut-être adapter cette logique)
            // Une façon de le faire serait d'ajouter une variable globale dans votre classe
            // qui est mise à jour par StartTableViewer
            return TableViewerStarted; // Vous devrez créer cette propriété
        }

        private void btnSmokeLimiter_ItemClick(object sender, ItemClickEventArgs e)
        {
            StartTableViewer("Smoke limiter", 2);
           
        }

        private void OpenMapByAddressDirect(int address)
        {
            SymbolCollection sc = (SymbolCollection)gridControl1.DataSource;
            int rtel = 0;
            bool found = false;

            foreach (SymbolHelper sh in sc)
            {
                if (sh.Flash_start_address == address)
                {
                    SélectionnerSymbole(rtel);
                    StartTableViewer();
                    found = true;
                    break;
                }
                rtel++;
            }

            if (!found)
            {
                throw new Exception("Adresse non trouvée");
            }
        }
        private void btnTargetBoost_ItemClick(object sender, ItemClickEventArgs e)
        {
            StartTableViewer("Boost target map", 2);
        }

        private void btnBoostPressureLimiter_ItemClick(object sender, ItemClickEventArgs e)
        {
            StartTableViewer("Boost limit map", 2);
        }

        private void btnBoostPressureLimitSVBL_ItemClick(object sender, ItemClickEventArgs e)
        {
            StartTableViewer("SVBL Boost limiter", 2);
        }

        private void btnN75Map_ItemClick(object sender, ItemClickEventArgs e)
        {
            StartTableViewer("N75 duty cycle", 2);
        }

        private void editXAxisToolStripMenuItem_Click(object sender, EventArgs e)
        {
            // 
            object o = gridViewSymbols.GetFocusedRow();
            if (o is SymbolHelper)
            {
                SymbolHelper sh = (SymbolHelper)o;
                StartAxisViewer(sh, Axis.XAxis);//sh.X_axis_descr, sh.Y_axis_address, sh.Y_axis_length, sh.Y_axis_ID);
            }
        }
        public enum Axis
        {
            XAxis,
            YAxis
        }
        private void StartAxisViewer(SymbolHelper symbol, Axis AxisToShow)//string Name, int address, int length, int axisID)
        {

            DevExpress.XtraBars.Docking.DockPanel dockPanel;
            dockManager1.BeginUpdate();
            try
            {


                dockPanel = dockManager1.AddPanel(DevExpress.XtraBars.Docking.DockingStyle.Right);
                int dw = 650;
                dockPanel.FloatSize = new Size(dw, 900);
                dockPanel.Width = dw;
                dockPanel.Tag = Tools.Instance.m_currentfile;
                ctrlAxisEditor tabdet = new ctrlAxisEditor();
                tabdet.FileName = Tools.Instance.m_currentfile;


                if (AxisToShow == Axis.XAxis)
                {
                    tabdet.AxisID = symbol.Y_axis_ID;
                    tabdet.AxisAddress = symbol.Y_axis_address;
                    tabdet.Map_name = symbol.X_axis_descr + " (" + symbol.Y_axis_address.ToString("X8") + ")";
                    int[] values = GetXaxisValues(Tools.Instance.m_currentfile, Tools.Instance.m_symbols, symbol.Varname);
                    float[] dataValues = new float[values.Length];
                    for (int i = 0; i < values.Length; i++)
                    {
                        float fValue = (float)Convert.ToDouble(values.GetValue(i)) * (float)symbol.X_axis_correction;
                        dataValues.SetValue(fValue, i);
                    }
                    tabdet.CorrectionFactor = (float)symbol.X_axis_correction;
                    tabdet.SetData(dataValues);
                    dockPanel.Text = "Axis: (X) " + tabdet.Map_name + " [" + Path.GetFileName(Tools.Instance.m_currentfile) + "]";
                }
                else if (AxisToShow == Axis.YAxis)
                {
                    tabdet.AxisID = symbol.X_axis_ID;
                    tabdet.AxisAddress = symbol.X_axis_address;
                    tabdet.Map_name = symbol.Y_axis_descr + " (" + symbol.X_axis_address.ToString("X8") + ")";
                    int[] values = GetYaxisValues(Tools.Instance.m_currentfile, Tools.Instance.m_symbols, symbol.Varname);
                    float[] dataValues = new float[values.Length];
                    for (int i = 0; i < values.Length; i++)
                    {
                        float fValue = (float)Convert.ToDouble(values.GetValue(i)) * (float)symbol.Y_axis_correction;
                        dataValues.SetValue(fValue, i);
                    }
                    tabdet.CorrectionFactor = (float)symbol.Y_axis_correction;
                    tabdet.SetData(dataValues);
                    dockPanel.Text = "Axis: (Y) " + tabdet.Map_name + " [" + Path.GetFileName(Tools.Instance.m_currentfile) + "]";
                }

                tabdet.onClose += new ctrlAxisEditor.ViewerClose(axis_Close);
                tabdet.onSave += new ctrlAxisEditor.DataSave(axis_Save);
                tabdet.Dock = DockStyle.Fill;
                dockPanel.Controls.Add(tabdet);
            }
            catch (Exception newdockE)
            {
                Console.WriteLine(newdockE.Message);
            }
            dockManager1.EndUpdate();

            System.Windows.Forms.Application.DoEvents();
        }
        private bool IsMapAlreadyCreated(string mapName, SymbolCollection symbols)
        {
            // Vérifier si la carte existe déjà avec le nom exact ou "nom (créé)"
            foreach (SymbolHelper sh in symbols)
            {
                if (sh.Varname == mapName ||
                    sh.Varname == mapName ||
                    sh.Userdescription == mapName ||
                    sh.Userdescription.Contains(mapName))
                {
                    return true;
                }
            }
            return false;
        }
        private void CreateDefaultMap(string mapName, string fileName)
        {
            // Définir les dimensions standard pour chaque type de carte
            int columns = 8;
            int rows = 16;
            byte[] defaultData;
            int address = 0;

            // Variables pour les axes
            int[] xAxisValues = null;
            int[] yAxisValues = null;
            byte[] xAxisData = null;
            byte[] yAxisData = null;
            int xAxisAddress = 0;
            int yAxisAddress = 0;

            // Personnaliser les dimensions et valeurs d'axes selon le type de carte
            if (mapName == "Driver wish")
            {
                columns = 8;  // 8 positions de pédale
                rows = 16;    // 16 régimes moteur

                // Définir les valeurs des axes
                xAxisValues = new int[] { 800, 1200, 1600, 2000, 2400, 2800, 3200, 3600, 4000, 4400, 4800, 5200, 5600, 6000, 6400, 6800 };
                yAxisValues = new int[] { 0, 5, 10, 15, 20, 25, 30, 35 };
            }
            else if (mapName == "Torque limiter")
            {
                columns = 20;
                rows = 4;

                // Définir les valeurs des axes
                xAxisValues = new int[] { 500,1000,1500,2000 };
                yAxisValues = new int[] { 1000, 1400, 1800, 2200, 2600, 3000, 3400, 3800, 4200, 4600, 5000, 5400,5800,6200,6600,7000,7400,7800,8200,8600 };
            }
            else if (mapName == "Smoke limiter")
            {
                columns = 12;
                rows = 16;

                // Définir les valeurs des axes
                xAxisValues = new int[] { 500,1000, 1500, 2000, 2500, 3000, 3500, 4000, 4500, 5000, 5500,6000,6500,7000,7500,8000 };
                yAxisValues = new int[] { 300, 350, 400, 450, 500, 550, 600, 650, 700, 750, 800, 850 };
            }
            else if (mapName == "Target boost")
            {
                columns = 10;
                rows = 16;

                // Définir les valeurs des axes
                xAxisValues = new int[] { 0,400,800,1200, 1600, 2000, 2400, 2800, 3200, 3600, 4000, 4400, 4800,5200,5600,6000 };
                yAxisValues = new int[] { 0,5, 10, 15,20, 30, 40, 50, 60, 70 };
            }
            else if (mapName == "Boost pressure limiter")
            {
                columns = 10;
                rows = 10;

                // Définir les valeurs des axes
                xAxisValues = new int[] { 1200, 1600, 2000, 2400, 2800, 3200, 3600, 4000, 4400, 4800 };
                yAxisValues = new int[] {1000,1500,2000,2500,3000,3500,4000,4500,5000,5500 };
            }
            else if (mapName == "Boost pressure guard" || mapName == "SVBL Boost limiter")
            {
                columns = 8;
                rows = 10;

                // Définir les valeurs des axes
                xAxisValues = new int[] { 1200, 1600, 2000, 2400, 2800, 3200, 3600, 4000, 4400, 4800 };
                yAxisValues = new int[] { 0, 10, 20, 30, 40, 50, 60, 70 };
            }
            else if (mapName == "N75 duty cycle")
            {
                columns = 12;
                rows = 16;

                // Définir les valeurs des axes
                xAxisValues = new int[] { 500,1000, 1500, 2000, 2500, 3000, 3500, 4000, 4500, 5000, 5500,6000,6500,7000,7500,8000 };
                yAxisValues = new int[] { 0,5, 10,15, 20,25, 30, 40, 50, 60, 70 };
            }
            else if (mapName == "EGR")
            {
                columns = 12;
                rows = 16;

                // Définir les valeurs des axes
                xAxisValues = new int[] { 500,1000, 1500, 2000, 2500, 3000, 3500, 4000, 4500, 5000, 5500,6000,6500,7000,7500,8000 };
                yAxisValues = new int[] { 5, 15,20, 25,30, 35,40, 45,50, 55, 65, 75 };
            }
            else if (mapName == "Injector duration")
            {
                columns = 10;
                rows = 10;

                // Définir les valeurs des axes
                xAxisValues = new int[] { 800, 1300, 1800, 2300, 2800, 3300, 3800, 4300, 4800, 5300 };
                yAxisValues = new int[] { 5, 15, 25, 35, 45, 55, 65, 75,80,85 };
            }
            else if (mapName == "Start of injection")
            {
                columns = 12;
                rows = 16;

                // Définir les valeurs des axes
                xAxisValues = new int[] { 800, 1300, 1800, 2300, 2800, 3300, 3800, 4300, 4800, 5300,5800,6300,6800,7300,7800,8300 };
                yAxisValues = new int[] { 5,10, 15,20, 25, 35, 45,50, 55,60, 65, 75 };
            }
            else if (mapName == "IQ by MAP limiter")
            {
                columns = 8;
                rows = 10;

                // Définir les valeurs des axes
                xAxisValues = new int[] { 1000, 1500, 2000, 2500, 3000, 3500, 4000, 4500, 5000, 5500 };
                yAxisValues = new int[] { 100, 200, 300, 400, 500, 600, 700, 800 }; // Valeurs de pression en mbar
            }
            else if (mapName == "IQ by MAF limiter")
            {
                columns = 8;
                rows = 10;

                // Définir les valeurs des axes
                xAxisValues = new int[] { 1000, 1500, 2000, 2500, 3000, 3500, 4000, 4500, 5000, 5500 };
                yAxisValues = new int[] { 200, 400, 600, 800, 1000, 1200, 1400, 1600 }; // Valeurs de débit d'air en mg/stroke
            }
            else if (mapName == "SOI limiter")
            {
                columns = 12;
                rows = 14;

                // Définir les valeurs des axes
                xAxisValues = new int[] { 500,1000, 1500, 2000, 2500, 3000, 3500, 4000, 4500, 5000, 5500,6000,6500,7000 };
                yAxisValues = new int[] { 5,10, 20, 30, 40, 50, 60, 70, 80,90,100,110 };
            }
            else if (mapName == "Start IQ")
            {
                columns = 6;
                rows = 8;

                // Définir les valeurs des axes
                xAxisValues = new int[] { -20, -10, 0, 10, 20, 30, 40, 50 }; // Température en °C
                yAxisValues = new int[] { 0, 600, 1200, 1800, 2400, 3000 }; // Régime moteur au démarrage
            }
            else
            {
                // Valeurs par défaut pour les cartes non spécifiées
                columns = 8;
                rows = 10;

                // Définir les valeurs des axes par défaut
                xAxisValues = new int[] { 1000, 1500, 2000, 2500, 3000, 3500, 4000, 4500, 5000, 5500 };
                yAxisValues = new int[] { 10, 20, 30, 40, 50, 60, 70, 80 };
            }

            // Calculer la taille totale nécessaire (2 octets par valeur pour format 16-bit)
            int totalSize = columns * rows * 2;
            defaultData = new byte[totalSize];

            // Initialiser toutes les valeurs à zéro
            for (int i = 0; i < totalSize; i++)
            {
                defaultData[i] = 0;
            }

            // Créer les données pour les axes
            if (xAxisValues != null && yAxisValues != null)
            {
                // Axe X
                xAxisData = new byte[rows * 2]; // 2 octets par valeur
                for (int i = 0; i < rows; i++)
                {
                    if (i < xAxisValues.Length)
                    {
                        xAxisData[i * 2] = (byte)(xAxisValues[i] >> 8);     // High byte
                        xAxisData[i * 2 + 1] = (byte)(xAxisValues[i] & 0xFF); // Low byte
                    }
                    else
                    {
                        // Valeurs par défaut si pas assez de valeurs définies
                        xAxisData[i * 2] = 0;
                        xAxisData[i * 2 + 1] = 0;
                    }
                }

                // Axe Y
                yAxisData = new byte[columns * 2]; // 2 octets par valeur
                for (int i = 0; i < columns; i++)
                {
                    if (i < yAxisValues.Length)
                    {
                        yAxisData[i * 2] = (byte)(yAxisValues[i] >> 8);     // High byte
                        yAxisData[i * 2 + 1] = (byte)(yAxisValues[i] & 0xFF); // Low byte
                    }
                    else
                    {
                        // Valeurs par défaut si pas assez de valeurs définies
                        yAxisData[i * 2] = 0;
                        yAxisData[i * 2 + 1] = 0;
                    }
                }
            }

            // Trouver un espace libre dans le fichier binaire pour la carte
            address = FindFreeSpace(fileName, totalSize);
            if (address == 0)
            {
                MessageBox.Show("Impossible de trouver un espace libre dans le fichier pour créer la carte.",
                                "Erreur", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            // Trouver des espaces libres pour les axes
            if (xAxisData != null)
            {
                xAxisAddress = FindFreeSpace(fileName, xAxisData.Length, address + totalSize);
                if (xAxisAddress == 0)
                {
                    MessageBox.Show("Impossible de trouver un espace libre pour l'axe X.",
                                    "Erreur", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
            }

            if (yAxisData != null)
            {
                yAxisAddress = FindFreeSpace(fileName, yAxisData.Length, xAxisAddress + (xAxisData?.Length ?? 0));
                if (yAxisAddress == 0)
                {
                    MessageBox.Show("Impossible de trouver un espace libre pour l'axe Y.",
                                    "Erreur", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
            }

            // Créer une nouvelle entrée dans la collection de symboles
            SymbolHelper newSymbol = new SymbolHelper();
            newSymbol.Varname = mapName;
            newSymbol.Flash_start_address = address;
            newSymbol.Length = totalSize;
            newSymbol.X_axis_length = rows;
            newSymbol.Y_axis_length = columns;

            // Configurer les axes
            if (xAxisAddress > 0)
            {
                newSymbol.X_axis_address = xAxisAddress;
            }

            if (yAxisAddress > 0)
            {
                newSymbol.Y_axis_address = yAxisAddress;
            }

            // Ajouter des descriptions selon le type de carte
            if (mapName == "Driver wish")
            {
                newSymbol.X_axis_descr = "Throttle position";
                newSymbol.Y_axis_descr = "Engine speed (rpm)";
                newSymbol.Z_axis_descr = "Requested IQ (mg)";
                newSymbol.XaxisUnits = "RPM";
                newSymbol.YaxisUnits = "%";
                newSymbol.Category = "Injection quantity";
                newSymbol.Subcategory = "Driver demand";
                newSymbol.Correction = 1.0;
                newSymbol.Offset = 0.0;
            }
            else if (mapName == "Torque limiter")
            {
                newSymbol.X_axis_descr = "Engine speed (rpm)";
                newSymbol.Y_axis_descr = "Atm. pressure (mbar)";
                newSymbol.Z_axis_descr = "Torque limit (Nm)";
                newSymbol.XaxisUnits = "RPM";
                newSymbol.YaxisUnits = "%";
                newSymbol.Category = "Injection quantity";
                newSymbol.Subcategory = "Limits";
                newSymbol.Correction = 1.0;
                newSymbol.Offset = 0.0;
            }
            else if (mapName == "Smoke limiter")
            {
                newSymbol.X_axis_descr = "AirFlow mg/stroke";
                newSymbol.Y_axis_descr = "Engine speed (rpm)";
                newSymbol.Z_axis_descr = "Maximum fuel (mg/cyl)";
                newSymbol.XaxisUnits = "mg";
                newSymbol.YaxisUnits = " RPM";
                newSymbol.Category = "Injection quantity";
                newSymbol.Subcategory = "Limits";
                newSymbol.Correction = 1.0;
                newSymbol.Offset = 0.0;
            }
            else if (mapName == "Target boost")
            {
                newSymbol.X_axis_descr = "IQ (mg/stroke)";
                newSymbol.Y_axis_descr = "Engine speed (rpm)";
                newSymbol.Z_axis_descr = "Target boost (mbar)";
                newSymbol.XaxisUnits = "mg";
                newSymbol.YaxisUnits = "RPM";
                newSymbol.Category = "Turbo";
                newSymbol.Subcategory = "Target";
                newSymbol.Correction = 1.0;
                newSymbol.Offset = 0.0;
            }
            else if (mapName == "Boost pressure limiter")
            {
                newSymbol.X_axis_descr = "Engine speed (rpm)";
                newSymbol.Y_axis_descr = "Atmospheric pressure (mbar)";
                newSymbol.Z_axis_descr = "Maximum boost (mbar)";
                newSymbol.XaxisUnits = "RPM";
                newSymbol.YaxisUnits = "%";
                newSymbol.Category = "Turbo";
                newSymbol.Subcategory = "Limiter";
                newSymbol.Correction = 1.0;
                newSymbol.Offset = 0.0;
            }
            else if (mapName == "Boost pressure guard" || mapName == "SVBL Boost limiter")
            {
                newSymbol.X_axis_descr = "Engine speed (rpm)";
                newSymbol.Y_axis_descr = "Load";
                newSymbol.Z_axis_descr = "SVBL limit (mbar)";
                newSymbol.XaxisUnits = "RPM";
                newSymbol.YaxisUnits = "%";
                newSymbol.Category = "Turbo";
                newSymbol.Subcategory = "SVBL";
                newSymbol.Correction = 1.0;
                newSymbol.Offset = 0.0;
            }
            else if (mapName == "N75 duty cycle")
            {
                newSymbol.X_axis_descr = "IQ ( mg/stroke)";
                newSymbol.Y_axis_descr = "Engine speed (rpm)";
                newSymbol.Z_axis_descr = "Duty cycle (%)";
                newSymbol.XaxisUnits = "mbar";
                newSymbol.YaxisUnits = "RPM";
                newSymbol.Category = "Turbo";
                newSymbol.Subcategory = "N75";
                newSymbol.Correction = 1.0;
                newSymbol.Offset = 0.0;
            }
            else if (mapName == "EGR")
            {
                newSymbol.X_axis_descr = "Engine speed (rpm)";
                newSymbol.Y_axis_descr = "Load";
                newSymbol.Z_axis_descr = "EGR rate (%)";
                newSymbol.XaxisUnits = "RPM";
                newSymbol.YaxisUnits = "%";
                newSymbol.Category = "Turbo";
                newSymbol.Subcategory = "EGR";
                newSymbol.Correction = 1.0;
                newSymbol.Offset = 0.0;
            }
            else if (mapName == "Injector duration")
            {
                newSymbol.X_axis_descr = "Engine speed (rpm)";
                newSymbol.Y_axis_descr = "Injection quantity (mg)";
                newSymbol.Z_axis_descr = "Duration (µs)";
                newSymbol.XaxisUnits = "RPM";
                newSymbol.YaxisUnits = "mg";
                newSymbol.Category = "Injection quantity";
                newSymbol.Subcategory = "Timing";
                newSymbol.Correction = 1.0;
                newSymbol.Offset = 0.0;
            }
            else if (mapName == "Start of injection")
            {
                newSymbol.X_axis_descr = "Engine speed (rpm)";
                newSymbol.Y_axis_descr = "Injection quantity (mg)";
                newSymbol.Z_axis_descr = "Start of injection (°BTDC)";
                newSymbol.XaxisUnits = "RPM";
                newSymbol.YaxisUnits = "mg";
                newSymbol.Category = "Injection quantity";
                newSymbol.Subcategory = "Timing";
                newSymbol.Correction = 1.0;
                newSymbol.Offset = 0.0;
            }
            else if (mapName == "IQ by MAP limiter")
            {
                newSymbol.X_axis_descr = "Engine speed (rpm)";
                newSymbol.Y_axis_descr = "MAP (mbar)";
                newSymbol.Z_axis_descr = "Maximum IQ (mg)";
                newSymbol.XaxisUnits = "RPM";
                newSymbol.YaxisUnits = "mbar";
                newSymbol.Category = "Injection quantity";
                newSymbol.Subcategory = "Limits";
                newSymbol.Correction = 1.0;
                newSymbol.Offset = 0.0;
            }
            else if (mapName == "IQ by MAF limiter")
            {
                newSymbol.X_axis_descr = "Engine speed (rpm)";
                newSymbol.Y_axis_descr = "Air mass (mg/stroke)";
                newSymbol.Z_axis_descr = "Maximum IQ (mg)";
                newSymbol.XaxisUnits = "RPM";
                newSymbol.YaxisUnits = "mg/stroke";
                newSymbol.Category = "Injection quantity";
                newSymbol.Subcategory = "Limits";
                newSymbol.Correction = 1.0;
                newSymbol.Offset = 0.0;
            }
            else if (mapName == "SOI limiter")
            {
                newSymbol.X_axis_descr = "Engine speed (rpm)";
                newSymbol.Y_axis_descr = "Injection quantity (mg)";
                newSymbol.Z_axis_descr = "SOI limit (°BTDC)";
                newSymbol.XaxisUnits = "RPM";
                newSymbol.YaxisUnits = "mg";
                newSymbol.Category = "Injection quantity";
                newSymbol.Subcategory = "Timing";
                newSymbol.Correction = 1.0;
                newSymbol.Offset = 0.0;
            }
            else if (mapName == "Start IQ")
            {
                newSymbol.X_axis_descr = "Temperature (°C)";
                newSymbol.Y_axis_descr = "Engine speed (rpm)";
                newSymbol.Z_axis_descr = "Start injection quantity (mg)";
                newSymbol.XaxisUnits = "°C";
                newSymbol.YaxisUnits = "RPM";
                newSymbol.Category = "Injection quantity";
                newSymbol.Subcategory = "Start";
                newSymbol.Correction = 1.0;
                newSymbol.Offset = 0.0;
            }
            else
            {
                // Informations génériques pour les cartes non spécifiées
                newSymbol.X_axis_descr = "X Axis";
                newSymbol.Y_axis_descr = "Y Axis";
                newSymbol.Z_axis_descr = "Values";
                newSymbol.Category = "Custom";
                newSymbol.Subcategory = "Custom map";
                newSymbol.Correction = 1.0;
                newSymbol.Offset = 0.0;
            }

            // Écrire les données dans le fichier
            Tools.Instance.savedatatobinary(address, totalSize, defaultData, fileName, true, "Carte créée: " + mapName, Tools.Instance.m_currentFileType);

            // Écrire les données des axes
            if (xAxisData != null && xAxisAddress > 0)
            {
                Tools.Instance.savedatatobinary(xAxisAddress, xAxisData.Length, xAxisData, fileName, false, Tools.Instance.m_currentFileType);
            }

            if (yAxisData != null && yAxisAddress > 0)
            {
                Tools.Instance.savedatatobinary(yAxisAddress, yAxisData.Length, yAxisData, fileName, false, Tools.Instance.m_currentFileType);
            }

            // Ajouter le nouveau symbole à la collection
            Tools.Instance.m_symbols.Add(newSymbol);
            SaveCreatedMapMetadata(newSymbol, fileName);
            SaveCreatedMaps(fileName);
            SaveSymbolToBinaryFile(newSymbol, fileName);
            // Rafraîchir la grille
            gridControl1.RefreshDataSource();

            // Mettre à jour les checksums si nécessaire
            VerifyChecksum(fileName, false, false);
        }

        // Version améliorée de FindFreeSpace qui évite les zones déjà utilisées
        private int FindFreeSpace(string fileName, int requiredSize, int startFrom = 0)
        {
            // Lire le fichier binaire
            byte[] fileData = File.ReadAllBytes(fileName);

            // Chercher une séquence de FF ou 00 assez longue
            for (int i = startFrom; i < fileData.Length - requiredSize; i++)
            {
                bool spaceFound = true;

                // Vérifier si c'est une séquence de FF ou 00
                byte checkByte = fileData[i];
                if (checkByte != 0xFF && checkByte != 0x00)
                    continue;

                // Vérifier que toute la séquence est identique
                for (int j = 0; j < requiredSize; j++)
                {
                    if (fileData[i + j] != checkByte)
                    {
                        spaceFound = false;
                        break;
                    }
                }

                if (spaceFound)
                    return i;
            }

            return 0; // Aucun espace trouvé
        }

        // Trouver un espace libre dans le fichier
        private int FindFreeSpace(string fileName, int requiredSize)
        {
            // Lire le fichier binaire
            byte[] fileData = File.ReadAllBytes(fileName);

            // Chercher une séquence de FF ou 00 assez longue
            for (int i = 0; i < fileData.Length - requiredSize; i++)
            {
                bool spaceFound = true;

                // Vérifier si c'est une séquence de FF ou 00
                byte checkByte = fileData[i];
                if (checkByte != 0xFF && checkByte != 0x00)
                    continue;

                // Vérifier que toute la séquence est identique
                for (int j = 0; j < requiredSize; j++)
                {
                    if (fileData[i + j] != checkByte)
                    {
                        spaceFound = false;
                        break;
                    }
                }

                if (spaceFound)
                    return i;
            }

            return 0; // Aucun espace trouvé
        }
        void axis_Save(object sender, EventArgs e)
        {
            if (sender is ctrlAxisEditor)
            {
                ctrlAxisEditor editor = (ctrlAxisEditor)sender;
                // recalculate the values back and store it in the file at the correct location
                float[] newvalues = editor.GetData();
                // well.. recalculate the data based on these new values
                //editor.CorrectionFactor
                int[] iValues = new int[newvalues.Length];
                // calculate back to integer values
                for (int i = 0; i < newvalues.Length; i++)
                {
                    int iValue = Convert.ToInt32(Convert.ToDouble(newvalues.GetValue(i))/editor.CorrectionFactor);
                    iValues.SetValue(iValue, i);
                }
                byte[] barr = new byte[iValues.Length * 2];
                int bCount = 0;
                for (int i = 0; i < iValues.Length; i++)
                {
                    int iVal = (int)iValues.GetValue(i);
                    byte b1 = (byte)((iVal & 0x00FF00) / 256);
                    byte b2 = (byte)(iVal & 0x0000FF);
                    barr[bCount++] = b1;
                    barr[bCount++] = b2;
                }
                string note = string.Empty;
                if (m_appSettings.RequestProjectNotes && Tools.Instance.m_CurrentWorkingProject != "")
                {
                    //request a small note from the user in which he/she can denote a description of the change
                    frmChangeNote changenote = new frmChangeNote();
                    changenote.ShowDialog();
                    note = changenote.Note;
                }
                SaveAxisDataIncludingSyncOption(editor.AxisAddress, barr.Length, barr, Tools.Instance.m_currentfile, true, note);
                // and we need to update mapviewers maybe?
                UpdateOpenViewers(Tools.Instance.m_currentfile);
            }
        }


        private void UpdateViewer(MapViewerEx tabdet)
        {
            string mapname = tabdet.Map_name;
            if (tabdet.Filename == Tools.Instance.m_currentfile)
            {
                foreach (SymbolHelper sh in Tools.Instance.m_symbols)
                {
                    if (sh.Varname == mapname)
                    {
                        // refresh data and axis in the viewer
                        SymbolAxesTranslator axestrans = new SymbolAxesTranslator();
                        string x_axis = string.Empty;
                        string y_axis = string.Empty;
                        string x_axis_descr = string.Empty;
                        string y_axis_descr = string.Empty;
                        string z_axis_descr = string.Empty;
                        tabdet.X_axis_name = sh.X_axis_descr;
                        tabdet.Y_axis_name = sh.Y_axis_descr;
                        tabdet.Z_axis_name = sh.Z_axis_descr;
                        tabdet.X_axisAddress = sh.Y_axis_address;
                        tabdet.Y_axisAddress = sh.X_axis_address;
                        tabdet.Xaxiscorrectionfactor = sh.X_axis_correction;
                        tabdet.Yaxiscorrectionfactor = sh.Y_axis_correction;
                        tabdet.Xaxiscorrectionoffset = sh.X_axis_offset;
                        tabdet.Yaxiscorrectionoffset = sh.Y_axis_offset;
                        tabdet.X_axisvalues = GetXaxisValues(Tools.Instance.m_currentfile, Tools.Instance.m_symbols, tabdet.Map_name);
                        tabdet.Y_axisvalues = GetYaxisValues(Tools.Instance.m_currentfile, Tools.Instance.m_symbols, tabdet.Map_name);
                        int columns = 8;
                        int rows = 8;
                        int tablewidth = GetTableMatrixWitdhByName(Tools.Instance.m_currentfile, Tools.Instance.m_symbols, tabdet.Map_name, out columns, out rows);
                        int address = Convert.ToInt32(sh.Flash_start_address);
                        tabdet.ShowTable(columns, true);
                        break;
                    }
                }
            }
        }

        private void UpdateOpenViewers(string filename)
        {

            try
            {
                // convert feedback map in memory to byte[] in stead of float[]
                foreach (DevExpress.XtraBars.Docking.DockPanel pnl in dockManager1.Panels)
                {
                    if (pnl.Text.StartsWith("Symbol: "))
                    {
                        foreach (Control c in pnl.Controls)
                        {
                            if (c is MapViewerEx)
                            {
                                MapViewerEx vwr = (MapViewerEx)c;
                                if (vwr.Filename == filename || filename == string.Empty)
                                {
                                    UpdateViewer(vwr);
                                }
                            }
                            else if (c is DevExpress.XtraBars.Docking.DockPanel)
                            {
                                DevExpress.XtraBars.Docking.DockPanel tpnl = (DevExpress.XtraBars.Docking.DockPanel)c;
                                foreach (Control c2 in tpnl.Controls)
                                {
                                    if (c2 is MapViewerEx)
                                    {
                                        MapViewerEx vwr2 = (MapViewerEx)c2;
                                        if (vwr2.Filename == filename || filename == string.Empty)
                                        {
                                            UpdateViewer(vwr2);
                                        }
                                    }
                                }
                            }
                            else if (c is DevExpress.XtraBars.Docking.ControlContainer)
                            {
                                DevExpress.XtraBars.Docking.ControlContainer cntr = (DevExpress.XtraBars.Docking.ControlContainer)c;
                                foreach (Control c3 in cntr.Controls)
                                {
                                    if (c3 is MapViewerEx)
                                    {
                                        MapViewerEx vwr3 = (MapViewerEx)c3;
                                        if (vwr3.Filename == filename || filename == string.Empty)
                                        {
                                            UpdateViewer(vwr3);
                                        }
                                    }
                                }
                            }
                        }

                    }
                }
            }
            catch (Exception E)
            {
                Console.WriteLine("Refresh viewer error: " + E.Message);
            }
        }

        void axis_Close(object sender, EventArgs e)
        {
            tabdet_onClose(sender, EventArgs.Empty); // recast
        }
       
        private void editYAxisToolStripMenuItem_Click(object sender, EventArgs e)
        {
            object o = gridViewSymbols.GetFocusedRow();
            if (o is SymbolHelper)
            {
                SymbolHelper sh = (SymbolHelper)o;
                StartAxisViewer(sh, Axis.YAxis);
            }
        }

        private void contextMenuStrip1_Opening(object sender, CancelEventArgs e)
        {
            if (gvhi != null)
            {
                if (gvhi.InColumnPanel || gvhi.InFilterPanel || gvhi.InGroupPanel)
                {
                    e.Cancel = true;
                    return;
                }
            }
            if (gridViewSymbols.FocusedRowHandle < 0)
            {
                e.Cancel = true;
                return;
            }
            try
            {
                object o = gridViewSymbols.GetFocusedRow();
                
                if (o is SymbolHelper)
                {
                    SymbolHelper sh = (SymbolHelper)o;
                    if (sh.X_axis_address > 0 && sh.X_axis_length > 0)
                    {
                        editXAxisToolStripMenuItem.Enabled = true;
                        editXAxisToolStripMenuItem.Text = "Edit x axis (" + sh.X_axis_descr + " " + sh.Y_axis_address.ToString("X8") + ")";
                    }
                    else
                    {
                        editXAxisToolStripMenuItem.Enabled = false;
                        editYAxisToolStripMenuItem.Text = "Edit x axis";
                    }
                    if (sh.Y_axis_address > 0 && sh.Y_axis_length > 0)
                    {
                        editYAxisToolStripMenuItem.Enabled = true;
                        editYAxisToolStripMenuItem.Text = "Edit y axis (" + sh.Y_axis_descr + " " + sh.X_axis_address.ToString("X8") + ")";
                    }
                    else
                    {
                        editYAxisToolStripMenuItem.Enabled = false;
                        editYAxisToolStripMenuItem.Text = "Edit y axis";
                    }
                }
            }
            catch (Exception)
            {

            }
        }

        private void btnEGRMap_ItemClick(object sender, ItemClickEventArgs e)
        {
            StartTableViewer("EGR", 2);
        }

        DevExpress.XtraGrid.Views.Grid.ViewInfo.GridHitInfo gvhi;

        private void gridControl1_MouseMove(object sender, MouseEventArgs e)
        {
            gvhi = gridViewSymbols.CalcHitInfo(new Point(e.X, e.Y));
        }

        private bool CheckAllTablesAvailable()
        {
            bool retval = true;
            if (string.IsNullOrEmpty(Tools.Instance.m_currentfile) || !File.Exists(Tools.Instance.m_currentfile))
            {
                return false;
            }

            // Obtenir le type d'ECU
            string ecuType = GetECUType();

            // Vérifier les tables requises en fonction du type d'ECU
            if (ecuType.Contains("EDC16"))
            {
                // Pour EDC16, vérifier toutes les tables existantes
                if (MapsWithNameMissing("Torque limiter", Tools.Instance.m_symbols)) return false;
                if (MapsWithNameMissing("Smoke limiter", Tools.Instance.m_symbols)) return false;
                if (MapsWithNameMissing("Driver wish", Tools.Instance.m_symbols)) return false;
            }
            else if (ecuType.Contains("EDC17") || ecuType.Contains("MED17"))
            {
                // Pour EDC17 et MED17, vérifier seulement les tables essentielles
                // "Driver wish" est généralement présent dans tous les types
                if (MapsWithNameMissing("Driver wish", Tools.Instance.m_symbols)) return false;

                // Vérifier si au moins l'une des tables de limitation est présente
                bool hasLimiter = !MapsWithNameMissing("Torque limiter", Tools.Instance.m_symbols) ||
                                  !MapsWithNameMissing("Smoke limiter", Tools.Instance.m_symbols);

                if (!hasLimiter)
                {
                    // Chercher des tables alternatives pour EDC17
                    bool hasAlternativeLimiter = !MapsWithNameMissing("Torque limitation", Tools.Instance.m_symbols) ||
                                                !MapsWithNameMissing("Engine torque limiter", Tools.Instance.m_symbols) ||
                                                !MapsWithNameMissing("Maximum torque", Tools.Instance.m_symbols);

                    if (!hasAlternativeLimiter)
                        return false;
                }
            }
            else
            {
                // Pour les autres types d'ECU, vérifier seulement que "Driver wish" existe
                // qui est la table minimale nécessaire pour les calculs
                if (MapsWithNameMissing("Driver wish", Tools.Instance.m_symbols)) return false;
            }

            return retval;
        }


        // Méthode pour obtenir le type d'ECU actuel
        private string GetECUType()
        {
            try
            {
                // Essayer d'obtenir le parser pour le fichier actuel
                IEDCFileParser parser = Tools.Instance.GetParserForFile(Tools.Instance.m_currentfile, false);
                if (parser == null)
                    return "Unknown";

                // Lire les données du fichier
                byte[] allBytes = File.ReadAllBytes(Tools.Instance.m_currentfile);
                string bpn = parser.ExtractBoschPartnumber(allBytes);

                if (string.IsNullOrEmpty(bpn))
                    return "Unknown";

                // Convertir le numéro de pièce en informations ECU
                partNumberConverter pnc = new partNumberConverter();
                ECUInfo info = pnc.ConvertPartnumber(bpn, allBytes.Length);

                return (info != null) ? info.EcuType : "Unknown";
            }
            catch (Exception)
            {
                return "Unknown";
            }
        }


        // Méthode 1: btnAirmassResult_ItemClick - Modifiée pour ajouter l'initialisation de PowerUnit
        // Dans la classe qui contient airmassResult


        private void btnAirmassResult_ItemClick(object sender, ItemClickEventArgs e)
        {
            DevExpress.XtraBars.Docking.DockPanel dockPanel;
            try
            {
                if (!CheckAllTablesAvailable())
                {
                    // Liste des tables vérifiées et manquantes
                    List<string> missingTables = new List<string>();

                    // Vérifier chaque table individuellement et lister celles qui manquent
                    if (MapsWithNameMissing("Driver wish", Tools.Instance.m_symbols))
                        missingTables.Add("Driver wish");
                    if (MapsWithNameMissing("Torque limiter", Tools.Instance.m_symbols))
                        missingTables.Add("Torque limiter");
                    if (MapsWithNameMissing("Smoke limiter", Tools.Instance.m_symbols))
                        missingTables.Add("Smoke limiter");

                    string missingTablesList = string.Join(", ", missingTables.ToArray());

                    // Demander à l'utilisateur s'il souhaite continuer malgré tout
                    DialogResult result = XtraMessageBox.Show(
                        $"Certaines tables requises sont manquantes: {missingTablesList}.\n\n" +
                        "Voulez-vous quand même tenter de calculer les résultats de performance ? " +
                        "Les résultats pourraient être incomplets ou inexacts.",
                        "Tables manquantes",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Warning);

                    if (result == DialogResult.No)
                        return;
                }

                dockManager1.BeginUpdate();
                try
                {
                    ctrlAirmassResult airmassResult = new ctrlAirmassResult();
                    airmassResult.Dock = DockStyle.Fill;
                    dockPanel = dockManager1.AddPanel(DevExpress.XtraBars.Docking.DockingStyle.Right);
                    dockPanel.Tag = Tools.Instance.m_currentfile;
                    dockPanel.ClosedPanel += new DevExpress.XtraBars.Docking.DockPanelEventHandler(dockPanel_ClosedPanel);
                    dockPanel.Text = "Résultats d'analyse de masse d'air: " + Path.GetFileName(Tools.Instance.m_currentfile);
                    dockPanel.Width = 800;
                    airmassResult.onStartTableViewer += new ctrlAirmassResult.StartTableViewer(airmassResult_onStartTableViewer);
                    airmassResult.onClose += new ctrlAirmassResult.ViewerClose(airmassResult_onClose);
                    airmassResult.Currentfile = Tools.Instance.m_currentfile;
                    airmassResult.Symbols = Tools.Instance.m_symbols;
                    airmassResult.Currentfile_size = Tools.Instance.m_currentfilelength;

                    // Vérifier si le fichier existe
                    if (!File.Exists(Tools.Instance.m_currentfile))
                    {
                        throw new FileNotFoundException("Le fichier actuel n'existe pas ou ne peut pas être accédé.");
                    }

                    // Extraire les informations du fichier
                    IEDCFileParser parser = Tools.Instance.GetParserForFile(Tools.Instance.m_currentfile, false);
                    if (parser == null)
                    {
                        throw new InvalidOperationException("Impossible de trouver un parser approprié pour ce fichier.");
                    }

                    byte[] allBytes = File.ReadAllBytes(Tools.Instance.m_currentfile);
                    string additionalInfo = parser.ExtractInfo(allBytes);

                    // Obtenir le numéro de pièce Bosch et les informations ECU
                    string bpn = parser.ExtractBoschPartnumber(allBytes);
                    if (string.IsNullOrEmpty(bpn))
                    {
                        throw new InvalidDataException("Impossible d'extraire le numéro de pièce Bosch du fichier.");
                    }

                    partNumberConverter pnc = new partNumberConverter();
                    ECUInfo info = pnc.ConvertPartnumber(bpn, allBytes.Length);
                    if (info == null)
                    {
                        throw new InvalidDataException("Impossible de convertir le numéro de pièce en informations ECU.");
                    }

                    // Configurer les propriétés nécessaires
                    int cylinders = pnc.GetNumberOfCylinders(info.EngineType, additionalInfo);
                    if (cylinders <= 0)
                    {
                        throw new InvalidDataException("Impossible de déterminer le nombre de cylindres pour ce type de moteur.");
                    }

                    airmassResult.NumberCylinders = cylinders;
                    airmassResult.ECUType = info.EcuType;

                    // Définir l'unité de puissance en chevaux (ch) par défaut
                    airmassResult.PowerUnit = "ch";

                    // Calculer et afficher les résultats
                    PerformanceResults results = airmassResult.Calculate(Tools.Instance.m_currentfile, Tools.Instance.m_symbols);

                    // Vérifier si le calcul a retourné des résultats valides
                    if (results == null || (results.Torque == 0 && results.Horsepower == 0))
                    {
                        throw new InvalidOperationException("Le calcul de performance a retourné des résultats invalides ou nuls.");
                    }

                    dockPanel.Controls.Add(airmassResult);
                    airmassResult.AddPowerUnitToggleButton(dockPanel);
                }
                catch (Exception innerEx)
                {
                    // Enregistrer l'erreur détaillée
                    Console.WriteLine("Erreur dans la création du panneau: " + innerEx.Message);
                    Console.WriteLine("Trace de la pile: " + innerEx.StackTrace);

                    // Afficher un message convivial pour l'utilisateur
                    XtraMessageBox.Show("Échec de l'affichage des résultats de performance: " + innerEx.Message,
                        "Erreur", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                finally
                {
                    dockManager1.EndUpdate();
                }
            }
            catch (Exception outerEx)
            {
                // Gérer toutes les exceptions qui pourraient survenir avant même de commencer le traitement
                Console.WriteLine("Erreur critique dans le calcul de performance: " + outerEx.Message);
                Console.WriteLine("Trace de la pile: " + outerEx.StackTrace);

                XtraMessageBox.Show("Impossible de traiter le calcul de performance: " + outerEx.Message,
                    "Erreur critique", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        void airmassResult_onClose(object sender, EventArgs e)
        {
            // lookup the panel which cast this event
            if (sender is ctrlAirmassResult)
            {
                string dockpanelname = "Airmass result viewer: " + Path.GetFileName(Tools.Instance.m_currentfile);
                foreach (DevExpress.XtraBars.Docking.DockPanel dp in dockManager1.Panels)
                {
                    if (dp.Text == dockpanelname)
                    {
                        dockManager1.RemovePanel(dp);
                        break;
                    }
                }
            }
        }

        void airmassResult_onStartTableViewer(object sender, ctrlAirmassResult.StartTableViewerEventArgs e)
        {
            StartTableViewer(e.SymbolName, 2);
        }

        private void btnExportXDF_ItemClick(object sender, ItemClickEventArgs e)
        {
            SaveFileDialog saveFileDialog2 = new SaveFileDialog();
            saveFileDialog2.Filter = "XDF files|*.xdf";
            if (gridControl1.DataSource != null)
            {
                XDFWriter xdf = new XDFWriter();

                string filename = Path.Combine(Path.GetDirectoryName(Tools.Instance.m_currentfile), Path.GetFileNameWithoutExtension(Tools.Instance.m_currentfile));
                saveFileDialog2.FileName = filename;
                if (saveFileDialog2.ShowDialog() == DialogResult.OK)
                {
                    //filename += ".xdf";
                    filename = saveFileDialog2.FileName;

                    xdf.CreateXDF(filename, Tools.Instance.m_currentfile, Tools.Instance.m_currentfilelength, Tools.Instance.m_currentfilelength);
                    foreach (SymbolHelper sh in Tools.Instance.m_symbols)
                    {
                        if (sh.Flash_start_address != 0)
                        {
                            int fileoffset = (int)sh.Flash_start_address;
                            while (fileoffset > Tools.Instance.m_currentfilelength) fileoffset -= Tools.Instance.m_currentfilelength;
                            /*if (sh.Varname == "Pgm_mod!") // VSS vlag
                            {
                                xdf.AddFlag("VSS", sh.Flash_start_address, 0x07);
                            }*/
                            if (sh.Varname.StartsWith("SVBL"))
                            {
                                
                            }
                            else 
                            {
                                string xaxis = sh.X_axis_descr;
                                string yaxis = sh.Y_axis_descr;
                                string zaxis = sh.Z_axis_descr;
                                bool m_issixteenbit = true;
                                // special maps are:
                                int xaxisaddress = sh.X_axis_address;
                                int yaxisaddress = sh.Y_axis_address;
                                bool isxaxissixteenbit = true;
                                bool isyaxissixteenbit = true;
                                int columns = sh.X_axis_length;
                                int rows = sh.Y_axis_length;
                                //int tablewidth = GetTableMatrixWitdhByName(Tools.Instance.m_currentfile, Tools.Instance.m_symbols, sh.Varname, out columns, out rows);
                                xdf.AddTable(sh.Varname, sh.Description, XDFCategories.Fuel, xaxis, yaxis, zaxis, columns, rows, fileoffset, m_issixteenbit, xaxisaddress, yaxisaddress, isxaxissixteenbit, isyaxissixteenbit, 1.0F, 1.0F, 1.0F);

                            }
                            /*else
                            {
                                xdf.AddConstant(55, sh.Varname, XDFCategories.Idle, "Aantal", sh.Length, fileoffset, true);
                            }*/
                        }
                    }
                    // add some specific stuff
                    //int fileoffset2 = Tools.Instance.m_currentfile_size - 0x4C;

                    //xdf.AddTable("Vehice Security Code", "VSS code", XDFCategories.Idle, "", "", "", 1, 6, fileoffset2 /*0x3FFB4*/, false, 0, 0, false, false, 1.0F, 1.0F, 1.0F);

                    xdf.CloseFile();
                }
            }
        }

        // van t5
        void tabdet_onViewTypeChanged(object sender, MapViewerEx.ViewTypeChangedEventArgs e)
        {
            if (m_appSettings.SynchronizeMapviewers || m_appSettings.SynchronizeMapviewersDifferentMaps)
            {
                foreach (DevExpress.XtraBars.Docking.DockPanel pnl in dockManager1.Panels)
                {
                    foreach (Control c in pnl.Controls)
                    {
                        if (c is MapViewerEx)
                        {
                            if (c != sender)
                            {
                                MapViewerEx vwr = (MapViewerEx)c;
                                if (vwr.Map_name == e.Mapname || m_appSettings.SynchronizeMapviewersDifferentMaps)
                                {
                                    vwr.Viewtype = e.View;
                                    vwr.ReShowTable();
                                    vwr.Invalidate();
                                }
                            }
                        }
                        else if (c is DevExpress.XtraBars.Docking.DockPanel)
                        {
                            DevExpress.XtraBars.Docking.DockPanel tpnl = (DevExpress.XtraBars.Docking.DockPanel)c;
                            foreach (Control c2 in tpnl.Controls)
                            {
                                if (c2 is MapViewerEx)
                                {
                                    if (c2 != sender)
                                    {
                                        MapViewerEx vwr2 = (MapViewerEx)c2;
                                        if (vwr2.Map_name == e.Mapname || m_appSettings.SynchronizeMapviewersDifferentMaps)
                                        {
                                            vwr2.Viewtype = e.View;
                                            vwr2.ReShowTable();
                                            vwr2.Invalidate();
                                        }
                                    }
                                }
                            }
                        }
                        else if (c is DevExpress.XtraBars.Docking.ControlContainer)
                        {
                            DevExpress.XtraBars.Docking.ControlContainer cntr = (DevExpress.XtraBars.Docking.ControlContainer)c;
                            foreach (Control c3 in cntr.Controls)
                            {
                                if (c3 is MapViewerEx)
                                {
                                    if (c3 != sender)
                                    {
                                        MapViewerEx vwr3 = (MapViewerEx)c3;
                                        if (vwr3.Map_name == e.Mapname || m_appSettings.SynchronizeMapviewersDifferentMaps)
                                        {
                                            vwr3.Viewtype = e.View;
                                            vwr3.ReShowTable();
                                            vwr3.Invalidate();
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        void tabdet_onSurfaceGraphViewChangedEx(object sender, MapViewerEx.SurfaceGraphViewChangedEventArgsEx e)
        {
            if (m_appSettings.SynchronizeMapviewers || m_appSettings.SynchronizeMapviewersDifferentMaps)
            {
                foreach (DevExpress.XtraBars.Docking.DockPanel pnl in dockManager1.Panels)
                {
                    foreach (Control c in pnl.Controls)
                    {
                        if (c is MapViewerEx)
                        {
                            if (c != sender)
                            {
                                MapViewerEx vwr = (MapViewerEx)c;
                                if (vwr.Map_name == e.Mapname || m_appSettings.SynchronizeMapviewersDifferentMaps)
                                {
                                    vwr.SetSurfaceGraphViewEx(e.DepthX, e.DepthY, e.Zoom, e.Rotation, e.Elevation);
                                    vwr.Invalidate();
                                }
                            }
                        }
                        else if (c is DevExpress.XtraBars.Docking.DockPanel)
                        {
                            DevExpress.XtraBars.Docking.DockPanel tpnl = (DevExpress.XtraBars.Docking.DockPanel)c;
                            foreach (Control c2 in tpnl.Controls)
                            {
                                if (c2 is MapViewerEx)
                                {
                                    if (c2 != sender)
                                    {
                                        MapViewerEx vwr2 = (MapViewerEx)c2;
                                        if (vwr2.Map_name == e.Mapname || m_appSettings.SynchronizeMapviewersDifferentMaps)
                                        {
                                            vwr2.SetSurfaceGraphViewEx(e.DepthX, e.DepthY, e.Zoom, e.Rotation, e.Elevation);
                                            vwr2.Invalidate();
                                        }
                                    }
                                }
                            }
                        }
                        else if (c is DevExpress.XtraBars.Docking.ControlContainer)
                        {
                            DevExpress.XtraBars.Docking.ControlContainer cntr = (DevExpress.XtraBars.Docking.ControlContainer)c;
                            foreach (Control c3 in cntr.Controls)
                            {
                                if (c3 is MapViewerEx)
                                {
                                    if (c3 != sender)
                                    {
                                        MapViewerEx vwr3 = (MapViewerEx)c3;
                                        if (vwr3.Map_name == e.Mapname || m_appSettings.SynchronizeMapviewersDifferentMaps)
                                        {
                                            vwr3.SetSurfaceGraphViewEx(e.DepthX, e.DepthY, e.Zoom, e.Rotation, e.Elevation);
                                            vwr3.Invalidate();
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        void tabdet_onSplitterMoved(object sender, MapViewerEx.SplitterMovedEventArgs e)
        {
            if (m_appSettings.SynchronizeMapviewers || m_appSettings.SynchronizeMapviewersDifferentMaps)
            {
                // andere cell geselecteerd, doe dat ook bij andere viewers met hetzelfde symbool (mapname)
                foreach (DevExpress.XtraBars.Docking.DockPanel pnl in dockManager1.Panels)
                {
                    foreach (Control c in pnl.Controls)
                    {
                        if (c is MapViewerEx)
                        {
                            if (c != sender)
                            {
                                MapViewerEx vwr = (MapViewerEx)c;
                                if (vwr.Map_name == e.Mapname || m_appSettings.SynchronizeMapviewersDifferentMaps)
                                {
                                    vwr.SetSplitter(e.Panel1height, e.Panel2height, e.Splitdistance, e.Panel1collapsed, e.Panel2collapsed);
                                    vwr.Invalidate();
                                }
                            }
                        }
                        else if (c is DevExpress.XtraBars.Docking.DockPanel)
                        {
                            DevExpress.XtraBars.Docking.DockPanel tpnl = (DevExpress.XtraBars.Docking.DockPanel)c;
                            foreach (Control c2 in tpnl.Controls)
                            {
                                if (c2 is MapViewerEx)
                                {
                                    if (c2 != sender)
                                    {
                                        MapViewerEx vwr2 = (MapViewerEx)c2;
                                        if (vwr2.Map_name == e.Mapname || m_appSettings.SynchronizeMapviewersDifferentMaps)
                                        {
                                            vwr2.SetSplitter(e.Panel1height, e.Panel2height, e.Splitdistance, e.Panel1collapsed, e.Panel2collapsed);
                                            vwr2.Invalidate();
                                        }
                                    }
                                }
                            }
                        }
                        else if (c is DevExpress.XtraBars.Docking.ControlContainer)
                        {
                            DevExpress.XtraBars.Docking.ControlContainer cntr = (DevExpress.XtraBars.Docking.ControlContainer)c;
                            foreach (Control c3 in cntr.Controls)
                            {
                                if (c3 is MapViewerEx)
                                {
                                    if (c3 != sender)
                                    {
                                        MapViewerEx vwr3 = (MapViewerEx)c3;
                                        if (vwr3.Map_name == e.Mapname || m_appSettings.SynchronizeMapviewersDifferentMaps)
                                        {
                                            vwr3.SetSplitter(e.Panel1height, e.Panel2height, e.Splitdistance, e.Panel1collapsed, e.Panel2collapsed);
                                            vwr3.Invalidate();
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        void tabdet_onSelectionChanged(object sender, MapViewerEx.CellSelectionChangedEventArgs e)
        {
            if (m_appSettings.SynchronizeMapviewers || m_appSettings.SynchronizeMapviewersDifferentMaps)
            {
                // andere cell geselecteerd, doe dat ook bij andere viewers met hetzelfde symbool (mapname)
                foreach (DevExpress.XtraBars.Docking.DockPanel pnl in dockManager1.Panels)
                {
                    foreach (Control c in pnl.Controls)
                    {
                        if (c is MapViewerEx)
                        {
                            if (c != sender)
                            {
                                MapViewerEx vwr = (MapViewerEx)c;
                                if (vwr.Map_name == e.Mapname || m_appSettings.SynchronizeMapviewersDifferentMaps)
                                {
                                    vwr.SelectCell(e.Rowhandle, e.Colindex);
                                    vwr.Invalidate();
                                }
                            }
                        }
                        else if (c is DevExpress.XtraBars.Docking.DockPanel)
                        {
                            DevExpress.XtraBars.Docking.DockPanel tpnl = (DevExpress.XtraBars.Docking.DockPanel)c;
                            foreach (Control c2 in tpnl.Controls)
                            {
                                if (c2 is MapViewerEx)
                                {
                                    if (c2 != sender)
                                    {
                                        MapViewerEx vwr2 = (MapViewerEx)c2;
                                        if (vwr2.Map_name == e.Mapname || m_appSettings.SynchronizeMapviewersDifferentMaps)
                                        {
                                            vwr2.SelectCell(e.Rowhandle, e.Colindex);
                                            vwr2.Invalidate();
                                        }
                                    }
                                }
                            }
                        }
                        else if (c is DevExpress.XtraBars.Docking.ControlContainer)
                        {
                            DevExpress.XtraBars.Docking.ControlContainer cntr = (DevExpress.XtraBars.Docking.ControlContainer)c;
                            foreach (Control c3 in cntr.Controls)
                            {
                                if (c3 is MapViewerEx)
                                {
                                    if (c3 != sender)
                                    {
                                        MapViewerEx vwr3 = (MapViewerEx)c3;
                                        if (vwr3.Map_name == e.Mapname || m_appSettings.SynchronizeMapviewersDifferentMaps)
                                        {
                                            vwr3.SelectCell(e.Rowhandle, e.Colindex);
                                            vwr3.Invalidate();
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        private void SetMapSliderPosition(string filename, string symbolname, int sliderposition)
        {
            foreach (DevExpress.XtraBars.Docking.DockPanel pnl in dockManager1.Panels)
            {
                foreach (Control c in pnl.Controls)
                {
                    if (c is MapViewerEx)
                    {
                        MapViewerEx vwr = (MapViewerEx)c;
                        if (vwr.Map_name == symbolname)
                        {
                            vwr.SliderPosition = sliderposition;
                            vwr.Invalidate();
                        }
                    }
                    else if (c is DevExpress.XtraBars.Docking.DockPanel)
                    {
                        DevExpress.XtraBars.Docking.DockPanel tpnl = (DevExpress.XtraBars.Docking.DockPanel)c;
                        foreach (Control c2 in tpnl.Controls)
                        {
                            if (c2 is MapViewerEx)
                            {
                                MapViewerEx vwr2 = (MapViewerEx)c2;
                                if (vwr2.Map_name == symbolname)
                                {
                                    vwr2.SliderPosition = sliderposition;
                                    vwr2.Invalidate();
                                }
                            }
                        }
                    }
                    else if (c is DevExpress.XtraBars.Docking.ControlContainer)
                    {
                        DevExpress.XtraBars.Docking.ControlContainer cntr = (DevExpress.XtraBars.Docking.ControlContainer)c;
                        foreach (Control c3 in cntr.Controls)
                        {
                            if (c3 is MapViewerEx)
                            {
                                MapViewerEx vwr3 = (MapViewerEx)c3;
                                if (vwr3.Map_name == symbolname)
                                {
                                    vwr3.SliderPosition = sliderposition;
                                    vwr3.Invalidate();
                                }
                            }
                        }
                    }
                }
            }

        }

        void tabdet_onSliderMove(object sender, MapViewerEx.SliderMoveEventArgs e)
        {
            if (m_appSettings.SynchronizeMapviewers || m_appSettings.SynchronizeMapviewersDifferentMaps)
            {
                SetMapSliderPosition(e.Filename, e.SymbolName, e.SliderPosition);
            }
        }

        private void SetMapScale(string filename, string symbolname, int axismax, int lockmode)
        {
            foreach (DevExpress.XtraBars.Docking.DockPanel pnl in dockManager1.Panels)
            {
                foreach (Control c in pnl.Controls)
                {
                    if (c is MapViewerEx)
                    {
                        MapViewerEx vwr = (MapViewerEx)c;
                        if (vwr.Map_name == symbolname || m_appSettings.SynchronizeMapviewersDifferentMaps)
                        {
                            vwr.Max_y_axis_value = axismax;
                            //vwr.ReShowTable(false);
                            vwr.LockMode = lockmode;
                            vwr.Invalidate();
                        }
                    }
                    else if (c is DevExpress.XtraBars.Docking.DockPanel)
                    {
                        DevExpress.XtraBars.Docking.DockPanel tpnl = (DevExpress.XtraBars.Docking.DockPanel)c;
                        foreach (Control c2 in tpnl.Controls)
                        {
                            if (c2 is MapViewerEx)
                            {
                                MapViewerEx vwr2 = (MapViewerEx)c2;
                                if (vwr2.Map_name == symbolname || m_appSettings.SynchronizeMapviewersDifferentMaps)
                                {
                                    vwr2.Max_y_axis_value = axismax;
                                    //vwr2.ReShowTable(false);
                                    vwr2.LockMode = lockmode;
                                    vwr2.Invalidate();
                                }
                            }
                        }
                    }
                    else if (c is DevExpress.XtraBars.Docking.ControlContainer)
                    {
                        DevExpress.XtraBars.Docking.ControlContainer cntr = (DevExpress.XtraBars.Docking.ControlContainer)c;
                        foreach (Control c3 in cntr.Controls)
                        {
                            if (c3 is MapViewerEx)
                            {
                                MapViewerEx vwr3 = (MapViewerEx)c3;
                                if (vwr3.Map_name == symbolname || m_appSettings.SynchronizeMapviewersDifferentMaps)
                                {

                                    vwr3.Max_y_axis_value = axismax;
                                    vwr3.LockMode = lockmode;
                                    //vwr3.ReShowTable(false);
                                    vwr3.Invalidate();
                                }
                            }
                        }
                    }
                }
            }

        }

        private int FindMaxTableValue(string symbolname, int orgvalue)
        {
            int retval = orgvalue;
            foreach (DevExpress.XtraBars.Docking.DockPanel pnl in dockManager1.Panels)
            {
                foreach (Control c in pnl.Controls)
                {
                    if (c is MapViewerEx)
                    {
                        MapViewerEx vwr = (MapViewerEx)c;
                        if (vwr.Map_name == symbolname)
                        {
                            if (vwr.MaxValueInTable > retval) retval = vwr.MaxValueInTable;
                        }
                    }
                    else if (c is DevExpress.XtraBars.Docking.DockPanel)
                    {
                        DevExpress.XtraBars.Docking.DockPanel tpnl = (DevExpress.XtraBars.Docking.DockPanel)c;
                        foreach (Control c2 in tpnl.Controls)
                        {
                            if (c2 is MapViewerEx)
                            {
                                MapViewerEx vwr2 = (MapViewerEx)c2;
                                if (vwr2.Map_name == symbolname)
                                {
                                    if (vwr2.MaxValueInTable > retval) retval = vwr2.MaxValueInTable;
                                }
                            }
                        }
                    }
                    else if (c is DevExpress.XtraBars.Docking.ControlContainer)
                    {
                        DevExpress.XtraBars.Docking.ControlContainer cntr = (DevExpress.XtraBars.Docking.ControlContainer)c;
                        foreach (Control c3 in cntr.Controls)
                        {
                            if (c3 is MapViewerEx)
                            {
                                MapViewerEx vwr3 = (MapViewerEx)c3;
                                if (vwr3.Map_name == symbolname)
                                {
                                    if (vwr3.MaxValueInTable > retval) retval = vwr3.MaxValueInTable;
                                }
                            }
                        }
                    }
                }
            }
            return retval;
        }

        void tabdet_onAxisLock(object sender, MapViewerEx.AxisLockEventArgs e)
        {
            if (m_appSettings.SynchronizeMapviewers || m_appSettings.SynchronizeMapviewersDifferentMaps)
            {
                int axismaxvalue = e.AxisMaxValue;
                if (e.LockMode == 1)
                {
                    axismaxvalue = FindMaxTableValue(e.SymbolName, axismaxvalue);
                }
                SetMapScale(e.Filename, e.SymbolName, axismaxvalue, e.LockMode);
            }
        }

        private void btnActivateLaunchControl_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (Tools.Instance.m_currentfile != string.Empty)
            {
                if (File.Exists(Tools.Instance.m_currentfile))
                {
                    if (MapsWithNameMissing("Launch control map", Tools.Instance.m_symbols))
                    {
                        byte[] allBytes = File.ReadAllBytes(Tools.Instance.m_currentfile);
                        bool found = true;
                        int offset = 0;
                        while (found)
                        {
                            int LCAddress = Tools.Instance.findSequence(allBytes, offset, new byte[16] { 0xFF, 0xFF, 0x02, 0x00, 0x80, 0x00, 0x00, 0x0A, 0xFF, 0xFF, 0x02, 0x00, 0x00, 0x00, 0x70, 0x17 }, new byte[16] { 0, 0, 1, 1, 1, 1, 1, 1, 0, 0, 1, 1, 1, 1, 1, 1 });
                            if (LCAddress > 0)
                            {
                                //Console.WriteLine("Working on " + LCAddress.ToString("X8"));
                                btnActivateLaunchControl.Enabled = false;

                                byte[] saveByte = new byte[(0x0E * 2) + 2];
                                int i = 0;
                                saveByte.SetValue(Convert.ToByte(0), i++);
                                saveByte.SetValue(Convert.ToByte(0x0E), i++);
                                saveByte.SetValue(Convert.ToByte(0), i++);
                                saveByte.SetValue(Convert.ToByte(0), i++); // 1st value = 0 km/h
                                saveByte.SetValue(Convert.ToByte(0), i++);
                                saveByte.SetValue(Convert.ToByte(20), i++); // 2nd value = 6 km/h (6 / 0.000039 = 

                                saveByte.SetValue(Convert.ToByte(0), i++);
                                saveByte.SetValue(Convert.ToByte(40), i++); // 

                                saveByte.SetValue(Convert.ToByte(0), i++);
                                saveByte.SetValue(Convert.ToByte(60), i++); // 

                                saveByte.SetValue(Convert.ToByte(0), i++);
                                saveByte.SetValue(Convert.ToByte(80), i++); // 

                                saveByte.SetValue(Convert.ToByte(0), i++);
                                saveByte.SetValue(Convert.ToByte(100), i++); // 
                                saveByte.SetValue(Convert.ToByte(0), i++);
                                saveByte.SetValue(Convert.ToByte(120), i++); // 

                                saveByte.SetValue(Convert.ToByte(0), i++);
                                saveByte.SetValue(Convert.ToByte(140), i++); // 
                                saveByte.SetValue(Convert.ToByte(0), i++);
                                saveByte.SetValue(Convert.ToByte(160), i++); // 
                                saveByte.SetValue(Convert.ToByte(0), i++);
                                saveByte.SetValue(Convert.ToByte(180), i++); // 
                                saveByte.SetValue(Convert.ToByte(0), i++);
                                saveByte.SetValue(Convert.ToByte(200), i++); // 
                                saveByte.SetValue(Convert.ToByte(0), i++);
                                saveByte.SetValue(Convert.ToByte(220), i++); // 
                                saveByte.SetValue(Convert.ToByte(0), i++);
                                saveByte.SetValue(Convert.ToByte(240), i++); // 
                                saveByte.SetValue(Convert.ToByte(1), i++);
                                saveByte.SetValue(Convert.ToByte(4), i++); // 

                                Tools.Instance.savedatatobinary(LCAddress + 2, saveByte.Length, saveByte, Tools.Instance.m_currentfile, false, Tools.Instance.m_currentFileType);
                                // fill the map with default values as well!
                                VerifyChecksum(Tools.Instance.m_currentfile, false, false);
                                
                                offset = LCAddress + 1;


                            }
                            else found = false;
                        }
                    }
                }
            }
            Application.DoEvents();
            //C2 02 00 xx xx xx xx xx EC 02 00 70 17*/
            if (!btnActivateLaunchControl.Enabled)
            {
                Tools.Instance.m_symbols = DetectMaps(Tools.Instance.m_currentfile, out Tools.Instance.codeBlockList, out Tools.Instance.AxisList, false, true);
                gridControl1.DataSource = null;
                Application.DoEvents();
                gridControl1.DataSource = Tools.Instance.m_symbols;
                Application.DoEvents();
                try
                {
                    gridViewSymbols.ExpandAllGroups();
                }
                catch (Exception)
                {

                }
                Application.DoEvents();

            }

        }

        private void btnEditEEProm_ItemClick(object sender, ItemClickEventArgs e)
        {
            OpenFileDialog ofd = new OpenFileDialog();
            //ofd.Filter = "Binary files|*.bin";
            ofd.Multiselect = false;
            if (ofd.ShowDialog() == DialogResult.OK)
            {
                // check size .. should be 4kb
                FileInfo fi = new FileInfo(ofd.FileName);
                if (fi.Length == 512)
                {
                    frmEEPromEditor editor = new frmEEPromEditor();
                    editor.LoadFile(ofd.FileName);
                    editor.ShowDialog();
                }
            }
        }

        private void gridViewSymbols_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                if (gridViewSymbols.FocusedColumn.Name == gcSymbolUserdescription.Name)
                {
                    SaveAdditionalSymbols();
                }
                else
                {
                    // start the selected row
                    try
                    {
                        int[] selectedrows = gridViewSymbols.GetSelectedRows();
                        int grouplevel = gridViewSymbols.GetRowLevel((int)selectedrows.GetValue(0));
                        if (grouplevel >= gridViewSymbols.GroupCount)
                        {
                            if (gridViewSymbols.GetFocusedRow() is SymbolHelper)
                            {
                                SymbolHelper sh = (SymbolHelper)gridViewSymbols.GetFocusedRow();
                                StartTableViewer(sh.Varname, sh.CodeBlock);
                                //StartTableViewer();
                            }
                        }
                    }
                    catch (Exception E)
                    {
                        Console.WriteLine(E.Message);
                    }
                }

            }
        }

        private void btnMergeFiles_ItemClick(object sender, ItemClickEventArgs e)
        {
            frmBinmerger frmmerger = new frmBinmerger();
            frmmerger.ShowDialog();
        }

        private void btnSplitFiles_ItemClick(object sender, ItemClickEventArgs e)
        {

            if (Tools.Instance.m_currentfile != "")
            {
                if (File.Exists(Tools.Instance.m_currentfile))
                {
                    string path = Path.GetDirectoryName(Tools.Instance.m_currentfile);
                    FileInfo fi = new FileInfo(Tools.Instance.m_currentfile);
                    FileStream fs = File.Create(path + "\\chip2.bin");
                    BinaryWriter bw = new BinaryWriter(fs);
                    FileStream fs2 = File.Create(path + "\\chip1.bin");
                    BinaryWriter bw2 = new BinaryWriter(fs2);
                    FileStream fsi1 = File.OpenRead(Tools.Instance.m_currentfile);
                    BinaryReader br1 = new BinaryReader(fsi1);
                    bool toggle = false;
                    for (int tel = 0; tel < fi.Length; tel++)
                    {
                        Byte ib1 = br1.ReadByte();
                        if (!toggle)
                        {
                            toggle = true;
                            bw.Write(ib1);
                        }
                        else
                        {
                            toggle = false;
                            bw2.Write(ib1);
                        }
                    }
                    bw.Close();
                    bw2.Close();
                    fs.Close();
                    fs2.Close();
                    fsi1.Close();
                    br1.Close();
                    MessageBox.Show("File split to chip1.bin and chip2.bin");
                }
            }
        }

        private void btnBuildLibrary_ItemClick(object sender, ItemClickEventArgs e)
        {
            frmBrowseFiles browse = new frmBrowseFiles();
            browse.Show();
        }

        private void StartPDFFile(string file, string errormessage)
        {
            try
            {
                if (File.Exists(file))
                {
                    System.Diagnostics.Process.Start(file);
                }
                else
                {
                    MessageBox.Show(errormessage);
                }
            }
            catch (Exception E2)
            {
                Console.WriteLine(E2.Message);
            }
        }

        private void btnUserManual_ItemClick(object sender, ItemClickEventArgs e)
        {
            // start user manual PDF file
            StartPDFFile(Path.Combine(System.Windows.Forms.Application.StartupPath, "EDC15PSuite manual.pdf"), "EDC15P user manual could not be found or opened!");
            
        }
        private void btnEDC15PDocumentation_ItemClick(object sender, ItemClickEventArgs e)
        {
            StartPDFFile(Path.Combine(System.Windows.Forms.Application.StartupPath, "VAG EDC15P.pdf"), "EDC15P documentation could not be found or opened!");
        }

        

        private void barButtonItem1_ItemClick(object sender, ItemClickEventArgs e)
        {
            ImportDescriptorFile(ImportFileType.XML);
        }

        private void barButtonItem2_ItemClick(object sender, ItemClickEventArgs e)
        {
            ImportDescriptorFile(ImportFileType.A2L);
        }

        private void barButtonItem3_ItemClick(object sender, ItemClickEventArgs e)
        {
            ImportDescriptorFile(ImportFileType.CSV);
        }

        private void barButtonItem4_ItemClick(object sender, ItemClickEventArgs e)
        {
            ImportDescriptorFile(ImportFileType.AS2);
        }

        private void barButtonItem5_ItemClick(object sender, ItemClickEventArgs e)
        {
            ImportDescriptorFile(ImportFileType.Damos);
        }
        private void SélectionnerSymbole(int rtel)
        {
            gridViewSymbols.ActiveFilter.Clear();
            SymbolCollection sc = (SymbolCollection)gridControl1.DataSource;

            int rhandle = gridViewSymbols.GetRowHandle(rtel);
            gridViewSymbols.OptionsSelection.MultiSelect = true;
            gridViewSymbols.OptionsSelection.MultiSelectMode = DevExpress.XtraGrid.Views.Grid.GridMultiSelectMode.RowSelect;
            gridViewSymbols.ClearSelection();
            gridViewSymbols.SelectRow(rhandle);
            gridViewSymbols.MakeRowVisible(rhandle, true);
            gridViewSymbols.FocusedRowHandle = rhandle;
        }
        private void ImportDescriptorFile(ImportFileType importFileType)
        {
            OpenFileDialog ofd = new OpenFileDialog();
            ofd.Filter = "AS2 documents|*.as2";
            if (importFileType == ImportFileType.A2L) ofd.Filter = "A2L documents|*.a2l";
            else if (importFileType == ImportFileType.CSV) ofd.Filter = "CSV documents|*.csv";
            else if (importFileType == ImportFileType.Damos) ofd.Filter = "Damos documents|*.dam";
            else if (importFileType == ImportFileType.XML) ofd.Filter = "XML documents|*.xml";
            ofd.Multiselect = false;
            if (ofd.ShowDialog() == DialogResult.OK)
            {
                TryToLoadAdditionalSymbols(ofd.FileName, importFileType, Tools.Instance.m_symbols, false);
                gridControl1.DataSource = Tools.Instance.m_symbols;
                gridControl1.RefreshDataSource();
                SaveAdditionalSymbols();
            }
        }




        private void SaveAdditionalSymbols()
        {
            System.Data.DataTable dt = new System.Data.DataTable(Path.GetFileNameWithoutExtension(Tools.Instance.m_currentfile));
            dt.Columns.Add("SYMBOLNAME");
            dt.Columns.Add("SYMBOLNUMBER", Type.GetType("System.Int32"));
            dt.Columns.Add("FLASHADDRESS", Type.GetType("System.Int32"));
            dt.Columns.Add("DESCRIPTION");
            dt.Columns.Add("ISCREATED", Type.GetType("System.Boolean"));
            dt.Columns.Add("XAXISADDRESS", Type.GetType("System.Int32"));
            dt.Columns.Add("YAXISADDRESS", Type.GetType("System.Int32"));

            byte[] allBytes = File.ReadAllBytes(Tools.Instance.m_currentfile);
            string boschpartNumber = Tools.Instance.ExtractBoschPartnumber(allBytes);
            partNumberConverter pnc = new partNumberConverter();
            ECUInfo info = pnc.ConvertPartnumber(boschpartNumber, allBytes.Length);
            string checkstring = boschpartNumber + "_" + info.SoftwareID;

            string xmlfilename = Tools.Instance.GetWorkingDirectory() + "\\repository\\" +
                                 Path.GetFileNameWithoutExtension(Tools.Instance.m_currentfile) +
                                 File.GetCreationTime(Tools.Instance.m_currentfile).ToString("yyyyMMddHHmmss") +
                                 checkstring + ".xml";

            if (!Directory.Exists(Tools.Instance.GetWorkingDirectory() + "\\repository"))
            {
                Directory.CreateDirectory(Tools.Instance.GetWorkingDirectory() + "\\repository");
            }

            if (File.Exists(xmlfilename))
            {
                File.Delete(xmlfilename);
            }

            foreach (SymbolHelper sh in Tools.Instance.m_symbols)
            {
                bool isCreated = sh.Varname.Contains("(créé)");
                if (sh.Userdescription != "" || isCreated)
                {
                    dt.Rows.Add(
                        sh.Varname,
                        sh.Symbol_number,
                        sh.Flash_start_address,
                        sh.Userdescription,
                        isCreated,
                        sh.X_axis_address,
                        sh.Y_axis_address
                    );
                }
            }

            dt.WriteXml(xmlfilename);
        }
        private void RestoreCreatedMap(DataRow mapData, SymbolCollection symbols)
        {
            try
            {
                int address = Convert.ToInt32(mapData["FLASHADDRESS"]);
                string varname = mapData["SYMBOLNAME"].ToString();
                int xAxisAddress = 0;
                int yAxisAddress = 0;

                // Récupérer les adresses des axes si disponibles
                if (mapData.Table.Columns.Contains("XAXISADDRESS") && mapData["XAXISADDRESS"] != DBNull.Value)
                    xAxisAddress = Convert.ToInt32(mapData["XAXISADDRESS"]);

                if (mapData.Table.Columns.Contains("YAXISADDRESS") && mapData["YAXISADDRESS"] != DBNull.Value)
                    yAxisAddress = Convert.ToInt32(mapData["YAXISADDRESS"]);

                // Créer un nouveau symbole
                SymbolHelper newSymbol = new SymbolHelper();
                newSymbol.Varname = varname;
                newSymbol.Flash_start_address = address;
                newSymbol.Userdescription = mapData["DESCRIPTION"].ToString();

                // Associer les axes si disponibles
                if (xAxisAddress > 0)
                    newSymbol.X_axis_address = xAxisAddress;

                if (yAxisAddress > 0)
                    newSymbol.Y_axis_address = yAxisAddress;

                // Déterminer les paramètres par défaut en fonction du nom de la carte
                string baseName = varname.Replace(" (créé)", "");
                SetDefaultMapParameters(newSymbol, baseName);

                // Ajouter le symbole à la collection
                symbols.Add(newSymbol);

                Console.WriteLine("Carte créée restaurée: " + varname + " à l'adresse " + address.ToString("X8"));
            }
            catch (Exception ex)
            {
                Console.WriteLine("Erreur lors de la restauration d'une carte créée: " + ex.Message);
            }
        }
        private void SetDefaultMapParameters(SymbolHelper symbol, string mapName)
        {
            // Définir les dimensions et descriptions par défaut
            if (mapName.Contains("Driver wish"))
            {
                symbol.X_axis_length = 16;
                symbol.Y_axis_length = 8;
                symbol.Length = 16 * 8 * 2;
                symbol.X_axis_descr = "Engine speed (rpm)";
                symbol.Y_axis_descr = "Throttle position";
                symbol.Z_axis_descr = "Requested IQ (mg)";
                symbol.XaxisUnits = "RPM";
                symbol.YaxisUnits = "%";
                symbol.Category = "Injection quantity";
                symbol.Subcategory = "Driver demand";
                symbol.Correction = 1.0;
                symbol.Offset = 0.0;
            }
            else if (mapName.Contains("Torque limiter"))
            {
                symbol.X_axis_length = 12;
                symbol.Y_axis_length = 8;
                symbol.Length = 12 * 8 * 2;
                symbol.X_axis_descr = "Engine speed (rpm)";
                symbol.Y_axis_descr = "Load";
                symbol.Z_axis_descr = "Torque limit (Nm)";
                symbol.XaxisUnits = "RPM";
                symbol.YaxisUnits = "%";
                symbol.Category = "Injection quantity";
                symbol.Subcategory = "Limits";
                symbol.Correction = 1.0;
                symbol.Offset = 0.0;
            }
            else if (mapName.Contains("Smoke limiter"))
            {
                symbol.X_axis_length = 10;
                symbol.Y_axis_length = 8;
                symbol.Length = 10 * 8 * 2;
                symbol.X_axis_descr = "Engine speed (rpm)";
                symbol.Y_axis_descr = "Air mass (mg/cyl)";
                symbol.Z_axis_descr = "Maximum fuel (mg/cyl)";
                symbol.XaxisUnits = "RPM";
                symbol.YaxisUnits = "mg/cyl";
                symbol.Category = "Injection quantity";
                symbol.Subcategory = "Limits";
                symbol.Correction = 1.0;
                symbol.Offset = 0.0;
            }
            else if (mapName.Contains("Target boost") || mapName.Contains("Boost target"))
            {
                symbol.X_axis_length = 10;
                symbol.Y_axis_length = 8;
                symbol.Length = 10 * 8 * 2;
                symbol.X_axis_descr = "Engine speed (rpm)";
                symbol.Y_axis_descr = "Load";
                symbol.Z_axis_descr = "Target boost (mbar)";
                symbol.XaxisUnits = "RPM";
                symbol.YaxisUnits = "%";
                symbol.Category = "Turbo";
                symbol.Subcategory = "Target";
                symbol.Correction = 1.0;
                symbol.Offset = 0.0;
            }
            else if (mapName.Contains("Boost pressure limiter") || mapName.Contains("Boost limit"))
            {
                symbol.X_axis_length = 10;
                symbol.Y_axis_length = 8;
                symbol.Length = 10 * 8 * 2;
                symbol.X_axis_descr = "Engine speed (rpm)";
                symbol.Y_axis_descr = "Load";
                symbol.Z_axis_descr = "Maximum boost (mbar)";
                symbol.XaxisUnits = "RPM";
                symbol.YaxisUnits = "%";
                symbol.Category = "Turbo";
                symbol.Subcategory = "Limiter";
                symbol.Correction = 1.0;
                symbol.Offset = 0.0;
            }
            else if (mapName.Contains("Boost pressure guard") || mapName.Contains("SVBL"))
            {
                symbol.X_axis_length = 10;
                symbol.Y_axis_length = 8;
                symbol.Length = 10 * 8 * 2;
                symbol.X_axis_descr = "Engine speed (rpm)";
                symbol.Y_axis_descr = "Load";
                symbol.Z_axis_descr = "SVBL limit (mbar)";
                symbol.XaxisUnits = "RPM";
                symbol.YaxisUnits = "%";
                symbol.Category = "Turbo";
                symbol.Subcategory = "SVBL";
                symbol.Correction = 1.0;
                symbol.Offset = 0.0;
            }
            else if (mapName.Contains("N75 duty cycle") || mapName.Contains("N75 duty"))
            {
                symbol.X_axis_length = 10;
                symbol.Y_axis_length = 8;
                symbol.Length = 10 * 8 * 2;
                symbol.X_axis_descr = "Engine speed (rpm)";
                symbol.Y_axis_descr = "Boost error (mbar)";
                symbol.Z_axis_descr = "Duty cycle (%)";
                symbol.XaxisUnits = "RPM";
                symbol.YaxisUnits = "mbar";
                symbol.Category = "Turbo";
                symbol.Subcategory = "N75";
                symbol.Correction = 1.0;
                symbol.Offset = 0.0;
            }
            else if (mapName.Contains("EGR"))
            {
                symbol.X_axis_length = 10;
                symbol.Y_axis_length = 8;
                symbol.Length = 10 * 8 * 2;
                symbol.X_axis_descr = "Engine speed (rpm)";
                symbol.Y_axis_descr = "Load";
                symbol.Z_axis_descr = "EGR rate (%)";
                symbol.XaxisUnits = "RPM";
                symbol.YaxisUnits = "%";
                symbol.Category = "Turbo";
                symbol.Subcategory = "EGR";
                symbol.Correction = 1.0;
                symbol.Offset = 0.0;
            }
            else if (mapName.Contains("Injector duration") || mapName.Contains("Injection duration"))
            {
                symbol.X_axis_length = 10;
                symbol.Y_axis_length = 8;
                symbol.Length = 10 * 8 * 2;
                symbol.X_axis_descr = "Engine speed (rpm)";
                symbol.Y_axis_descr = "Injection quantity (mg)";
                symbol.Z_axis_descr = "Duration (µs)";
                symbol.XaxisUnits = "RPM";
                symbol.YaxisUnits = "mg";
                symbol.Category = "Injection quantity";
                symbol.Subcategory = "Timing";
                symbol.Correction = 1.0;
                symbol.Offset = 0.0;
            }
            else if (mapName.Contains("Start of injection") || mapName.Contains("SOI"))
            {
                symbol.X_axis_length = 10;
                symbol.Y_axis_length = 8;
                symbol.Length = 10 * 8 * 2;
                symbol.X_axis_descr = "Engine speed (rpm)";
                symbol.Y_axis_descr = "Injection quantity (mg)";
                symbol.Z_axis_descr = "Start of injection (°BTDC)";
                symbol.XaxisUnits = "RPM";
                symbol.YaxisUnits = "mg";
                symbol.Category = "Injection quantity";
                symbol.Subcategory = "Timing";
                symbol.Correction = 1.0;
                symbol.Offset = 0.0;
            }
            else if (mapName.Contains("IQ by MAP limiter"))
            {
                symbol.X_axis_length = 10;
                symbol.Y_axis_length = 8;
                symbol.Length = 10 * 8 * 2;
                symbol.X_axis_descr = "Engine speed (rpm)";
                symbol.Y_axis_descr = "MAP (mbar)";
                symbol.Z_axis_descr = "Maximum IQ (mg)";
                symbol.XaxisUnits = "RPM";
                symbol.YaxisUnits = "mbar";
                symbol.Category = "Injection quantity";
                symbol.Subcategory = "Limits";
                symbol.Correction = 1.0;
                symbol.Offset = 0.0;
            }
            else if (mapName.Contains("IQ by MAF limiter"))
            {
                symbol.X_axis_length = 10;
                symbol.Y_axis_length = 8;
                symbol.Length = 10 * 8 * 2;
                symbol.X_axis_descr = "Engine speed (rpm)";
                symbol.Y_axis_descr = "Air mass (mg/stroke)";
                symbol.Z_axis_descr = "Maximum IQ (mg)";
                symbol.XaxisUnits = "RPM";
                symbol.YaxisUnits = "mg/stroke";
                symbol.Category = "Injection quantity";
                symbol.Subcategory = "Limits";
                symbol.Correction = 1.0;
                symbol.Offset = 0.0;
            }
            else if (mapName.Contains("SOI limiter"))
            {
                symbol.X_axis_length = 10;
                symbol.Y_axis_length = 8;
                symbol.Length = 10 * 8 * 2;
                symbol.X_axis_descr = "Engine speed (rpm)";
                symbol.Y_axis_descr = "Injection quantity (mg)";
                symbol.Z_axis_descr = "SOI limit (°BTDC)";
                symbol.XaxisUnits = "RPM";
                symbol.YaxisUnits = "mg";
                symbol.Category = "Injection quantity";
                symbol.Subcategory = "Timing";
                symbol.Correction = 1.0;
                symbol.Offset = 0.0;
            }
            else if (mapName.Contains("Start IQ"))
            {
                symbol.X_axis_length = 8;
                symbol.Y_axis_length = 6;
                symbol.Length = 8 * 6 * 2;
                symbol.X_axis_descr = "Temperature (°C)";
                symbol.Y_axis_descr = "Engine speed (rpm)";
                symbol.Z_axis_descr = "Start injection quantity (mg)";
                symbol.XaxisUnits = "°C";
                symbol.YaxisUnits = "RPM";
                symbol.Category = "Injection quantity";
                symbol.Subcategory = "Start";
                symbol.Correction = 1.0;
                symbol.Offset = 0.0;
            }
            else if (mapName.Contains("Launch control"))
            {
                symbol.X_axis_length = 8;
                symbol.Y_axis_length = 6;
                symbol.Length = 8 * 6 * 2;
                symbol.X_axis_descr = "Gear";
                symbol.Y_axis_descr = "Speed (km/h)";
                symbol.Z_axis_descr = "RPM limit";
                symbol.XaxisUnits = "";
                symbol.YaxisUnits = "km/h";
                symbol.Category = "Special features";
                symbol.Subcategory = "Launch Control";
                symbol.Correction = 1.0;
                symbol.Offset = 0.0;
            }
            else if (mapName.Contains("Inverse driver wish"))
            {
                symbol.X_axis_length = 16;
                symbol.Y_axis_length = 8;
                symbol.Length = 16 * 8 * 2;
                symbol.X_axis_descr = "Engine speed (rpm)";
                symbol.Y_axis_descr = "Requested IQ (mg)";
                symbol.Z_axis_descr = "Throttle position (%)";
                symbol.XaxisUnits = "RPM";
                symbol.YaxisUnits = "mg";
                symbol.Category = "Injection quantity";
                symbol.Subcategory = "Driver demand";
                symbol.Correction = 1.0;
                symbol.Offset = 0.0;
            }
            else if (mapName.Contains("MAF correction"))
            {
                symbol.X_axis_length = 10;
                symbol.Y_axis_length = 8;
                symbol.Length = 10 * 8 * 2;
                symbol.X_axis_descr = "Engine speed (rpm)";
                symbol.Y_axis_descr = "Load";
                symbol.Z_axis_descr = "Correction (%)";
                symbol.XaxisUnits = "RPM";
                symbol.YaxisUnits = "%";
                symbol.Category = "Air mass";
                symbol.Subcategory = "Correction";
                symbol.Correction = 1.0;
                symbol.Offset = 0.0;
            }
            else if (mapName.Contains("MAF linearization"))
            {
                symbol.X_axis_length = 16;
                symbol.Y_axis_length = 1;
                symbol.Length = 16 * 2;
                symbol.X_axis_descr = "Sensor voltage (V)";
                symbol.Y_axis_descr = "";
                symbol.Z_axis_descr = "Air mass (mg/stroke)";
                symbol.XaxisUnits = "V";
                symbol.YaxisUnits = "";
                symbol.Category = "Air mass";
                symbol.Subcategory = "Sensor";
                symbol.Correction = 1.0;
                symbol.Offset = 0.0;
            }
            else if (mapName.Contains("MAP linearization"))
            {
                symbol.X_axis_length = 16;
                symbol.Y_axis_length = 1;
                symbol.Length = 16 * 2;
                symbol.X_axis_descr = "Sensor voltage (V)";
                symbol.Y_axis_descr = "";
                symbol.Z_axis_descr = "Pressure (mbar)";
                symbol.XaxisUnits = "V";
                symbol.YaxisUnits = "";
                symbol.Category = "Air mass";
                symbol.Subcategory = "Sensor";
                symbol.Correction = 1.0;
                symbol.Offset = 0.0;
            }
            else if (mapName.Contains("Boost correction"))
            {
                symbol.X_axis_length = 10;
                symbol.Y_axis_length = 6;
                symbol.Length = 10 * 6 * 2;
                symbol.X_axis_descr = "Engine speed (rpm)";
                symbol.Y_axis_descr = "Temperature (°C)";
                symbol.Z_axis_descr = "Correction (%)";
                symbol.XaxisUnits = "RPM";
                symbol.YaxisUnits = "°C";
                symbol.Category = "Turbo";
                symbol.Subcategory = "Correction";
                symbol.Correction = 1.0;
                symbol.Offset = 0.0;
            }
            else
            {
                // Paramètres par défaut si le type de carte n'est pas reconnu
                symbol.X_axis_length = 10;
                symbol.Y_axis_length = 8;
                symbol.Length = 10 * 8 * 2;
                symbol.X_axis_descr = "X Axis";
                symbol.Y_axis_descr = "Y Axis";
                symbol.Z_axis_descr = "Values";
                symbol.Category = "Custom";
                symbol.Subcategory = "Custom map";
                symbol.Correction = 1.0;
                symbol.Offset = 0.0;
            }
        }
        private void TryToLoadAdditionalSymbols(string filename, ImportFileType importFileType, SymbolCollection symbolCollection, bool fromRepository)
        {
            if (importFileType == ImportFileType.XML)
            {
                ImportXMLFile(filename, symbolCollection, fromRepository);
            }
            else if (importFileType == ImportFileType.AS2)
            {
                TryToLoadAdditionalAS2Symbols(filename, symbolCollection);
            }
            else if (importFileType == ImportFileType.CSV)
            {
                TryToLoadAdditionalCSVSymbols(filename, symbolCollection);
            }
        }

        private void TryToLoadAdditionalCSVSymbols(string filename, SymbolCollection coll2load)
        {
            // convert to CSV file format
            // ADDRESS;NAME;;;
            try
            {
                SymbolTranslator st = new SymbolTranslator();
                char[] sep = new char[1];
                sep.SetValue(';', 0);
                string[] fileContent = File.ReadAllLines(filename);
                foreach (string line in fileContent)
                {
                    string[] values = line.Split(sep);
                    try
                    {
                        string varname = (string)values.GetValue(1);
                        int flashaddress = Convert.ToInt32(values.GetValue(0));
                        foreach (SymbolHelper sh in coll2load)
                        {
                            if (sh.Flash_start_address == flashaddress)
                            {
                                sh.Userdescription = varname;
                            }
                        }
                    }
                    catch (Exception lineE)
                    {
                        Console.WriteLine("Failed to import a symbol from CSV file " + line + ": " + lineE.Message);
                    }
                }
            }
            catch (Exception E)
            {
                Console.WriteLine("Failed to import additional CSV symbols: " + E.Message);
            }
        }

        private void TryToLoadAdditionalAS2Symbols(string filename, SymbolCollection coll2load)
        {
            // convert to AS2 file format

            try
            {
                SymbolTranslator st = new SymbolTranslator();
                char[] sep = new char[1];
                sep.SetValue(';', 0);
                string[] fileContent = File.ReadAllLines(filename);
                int symbolnumber = 0;
                foreach (string line in fileContent)
                {
                    if (line.StartsWith("*"))
                    {
                        symbolnumber++;
                        string[] values = line.Split(sep);
                        try
                        {

                            string varname = (string)values.GetValue(0);
                            varname = varname.Substring(1);
                            int idxSymTab = 0;
                            foreach (SymbolHelper sh in coll2load)
                            {
                                if (sh.Length > 0) idxSymTab++;
                                if (idxSymTab == symbolnumber)
                                {
                                    sh.Userdescription = varname;
                                    break;
                                }
                            }
                        }
                        catch (Exception lineE)
                        {
                            Console.WriteLine("Failed to import a symbol from AS2 file " + line + ": " + lineE.Message);
                        }

                    }
                }
            }
            catch (Exception E)
            {
                Console.WriteLine("Failed to import additional AS2 symbols: " + E.Message);
            }
        }

        private bool ImportXMLFile(string filename, SymbolCollection coll2load, bool ImportFromRepository)
        {
            bool retval = false;
            SymbolTranslator st = new SymbolTranslator();
            System.Data.DataTable dt = new System.Data.DataTable(Path.GetFileNameWithoutExtension(filename));
            dt.Columns.Add("SYMBOLNAME");
            dt.Columns.Add("SYMBOLNUMBER", Type.GetType("System.Int32"));
            dt.Columns.Add("FLASHADDRESS", Type.GetType("System.Int32"));
            dt.Columns.Add("DESCRIPTION");
            dt.Columns.Add("ISCREATED", Type.GetType("System.Boolean"));
            dt.Columns.Add("XAXISADDRESS", Type.GetType("System.Int32"));
            dt.Columns.Add("YAXISADDRESS", Type.GetType("System.Int32"));
            if (ImportFromRepository)
            {
                byte[] allBytes = File.ReadAllBytes(filename);
                string boschpartNumber = Tools.Instance.ExtractBoschPartnumber(allBytes);
                partNumberConverter pnc = new partNumberConverter();
                ECUInfo info = pnc.ConvertPartnumber(boschpartNumber, allBytes.Length);
                string checkstring = boschpartNumber + "_" + info.SoftwareID;

                string xmlfilename = Tools.Instance.GetWorkingDirectory() + "\\repository\\" + Path.GetFileNameWithoutExtension(filename) + File.GetCreationTime(filename).ToString("yyyyMMddHHmmss") + checkstring + ".xml";
                if (!Directory.Exists(Tools.Instance.GetWorkingDirectory() + "\\repository"))
                {
                    Directory.CreateDirectory(Tools.Instance.GetWorkingDirectory() + "\\repository");
                }
                if (File.Exists(xmlfilename))
                {
                    dt.ReadXml(xmlfilename);
                    retval = true;
                }
            }
            else
            {
                string binname = GetFileDescriptionFromFile(filename);
                if (binname != string.Empty)
                {
                    dt = new System.Data.DataTable(binname);
                    dt.Columns.Add("SYMBOLNAME");
                    dt.Columns.Add("SYMBOLNUMBER", Type.GetType("System.Int32"));
                    dt.Columns.Add("FLASHADDRESS", Type.GetType("System.Int32"));
                    dt.Columns.Add("DESCRIPTION");
                    dt.Columns.Add("ISCREATED", Type.GetType("System.Boolean"));
                    dt.Columns.Add("XAXISADDRESS", Type.GetType("System.Int32"));
                    dt.Columns.Add("YAXISADDRESS", Type.GetType("System.Int32"));
                    if (File.Exists(filename))
                    {
                        dt.ReadXml(filename);
                        retval = true;
                    }
                }
            }
            foreach (SymbolHelper sh in coll2load)
            {
                foreach (DataRow dr in dt.Rows)
                {
                    try
                    {
                        // Vérifier si ce n'est pas une carte créée (pour les cartes existantes)
                        bool isCreated = false;
                        if (dt.Columns.Contains("ISCREATED"))
                        {
                            if (dr["ISCREATED"] != DBNull.Value)
                                isCreated = Convert.ToBoolean(dr["ISCREATED"]);
                        }

                        if (!isCreated)
                        {
                            if (sh.Flash_start_address == Convert.ToInt32(dr["FLASHADDRESS"]))
                            {
                                sh.Userdescription = dr["DESCRIPTION"].ToString();
                                break;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("Erreur lors du traitement d'un symbole existant: " + ex.Message);
                    }
                }
            }
            foreach (DataRow dr in dt.Rows)
            {
                try
                {
                    // Vérifier si c'est une carte créée
                    bool isCreated = false;
                    if (dt.Columns.Contains("ISCREATED"))
                    {
                        if (dr["ISCREATED"] != DBNull.Value)
                            isCreated = Convert.ToBoolean(dr["ISCREATED"]);
                    }
                    else if (dr["SYMBOLNAME"].ToString().Contains("(créé)"))
                    {
                        isCreated = true;
                    }

                    if (isCreated)
                    {
                        // Restaurer la carte créée si elle n'existe pas déjà
                        bool alreadyExists = false;
                        int address = Convert.ToInt32(dr["FLASHADDRESS"]);

                        foreach (SymbolHelper sh in coll2load)
                        {
                            if (sh.Flash_start_address == address)
                            {
                                alreadyExists = true;
                                break;
                            }
                        }

                        if (!alreadyExists)
                            RestoreCreatedMap(dr, coll2load);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Erreur lors de la restauration d'une carte créée: " + ex.Message);
                }
            }

            //return true;
            return retval;


        }

        private string GetFileDescriptionFromFile(string file)
        {
            string retval = string.Empty;
            try
            {
                using (StreamReader sr = new StreamReader(file))
                {
                    sr.ReadLine();
                    sr.ReadLine();
                    string name = sr.ReadLine();
                    name = name.Trim();
                    name = name.Replace("<", "");
                    name = name.Replace(">", "");
                    //name = name.Replace("x0020", " ");
                    name = name.Replace("_x0020_", " ");
                    for (int i = 0; i <= 9; i++)
                    {
                        name = name.Replace("_x003" + i.ToString() + "_", i.ToString());
                    }
                    retval = name;
                }
            }
            catch (Exception E)
            {
                Console.WriteLine(E.Message);
            }
            return retval;
        }

        private void gridViewSymbols_CellValueChanged(object sender, DevExpress.XtraGrid.Views.Base.CellValueChangedEventArgs e)
        {

            if (e.Column.Name == gcSymbolUserdescription.Name)
            {
                SaveAdditionalSymbols();
            }
        }

        private void dockManager1_LayoutUpgrade(object sender, DevExpress.Utils.LayoutUpgadeEventArgs e)
        {

        }

        private void btnActivateSmokeLimiters_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (Tools.Instance.m_currentfile != string.Empty)
            {
                if (File.Exists(Tools.Instance.m_currentfile))
                {
                    btnActivateSmokeLimiters.Enabled = false;
                    // find the smoke limiter control map (selector)
                    foreach (SymbolHelper sh in Tools.Instance.m_symbols)
                    {
                        if (sh.Varname.StartsWith("Smoke limiter"))
                        {
                            byte[] mapdata = new byte[sh.Length];
                            mapdata.Initialize();
                            mapdata = Tools.Instance.readdatafromfile(Tools.Instance.m_currentfile, (int)sh.Flash_start_address, sh.Length, Tools.Instance.m_currentFileType);

                            int selectorAddress = sh.MapSelector.StartAddress;
                            if (selectorAddress > 0)
                            {
                                byte[] mapIndexes = new byte[sh.MapSelector.MapIndexes.Length * 2];
                                int bIdx = 0;
                                for (int i = 0; i < sh.MapSelector.MapIndexes.Length; i++)
                                {
                                    mapIndexes[bIdx++] = Convert.ToByte(i);
                                    mapIndexes[bIdx++] = 0;
                                }
                                Tools.Instance.savedatatobinary(selectorAddress + mapIndexes.Length, mapIndexes.Length, mapIndexes, Tools.Instance.m_currentfile, false, Tools.Instance.m_currentFileType);
                            }
                            for (int i = 1; i < sh.MapSelector.MapIndexes.Length; i++)
                            {
                                // save the map data (copy)
                                int saveAddress = (int)sh.Flash_start_address + i * sh.Length;
                                Tools.Instance.savedatatobinary(saveAddress, sh.Length, mapdata, Tools.Instance.m_currentfile, false, Tools.Instance.m_currentFileType);
                            }
                        }
                    }

                    VerifyChecksum(Tools.Instance.m_currentfile, false, false);
                }
            }
            Application.DoEvents();

            if (!btnActivateSmokeLimiters.Enabled)
            {
                Tools.Instance.m_symbols = DetectMaps(Tools.Instance.m_currentfile, out Tools.Instance.codeBlockList, out Tools.Instance.AxisList, false, true);
                gridControl1.DataSource = null;
                Application.DoEvents();
                gridControl1.DataSource = Tools.Instance.m_symbols;
                Application.DoEvents();
                try
                {

                    gridViewSymbols.ExpandAllGroups();
                }
                catch (Exception)
                {

                }
                Application.DoEvents();

            }
        }

        private void ImportFileInExcelFormat()
        {
            OpenFileDialog openFileDialog2 = new OpenFileDialog();
            openFileDialog2.Multiselect = false;
            
            if (openFileDialog2.ShowDialog() == DialogResult.OK)
            {
                try
                {
                    string mapname = string.Empty;
                    string realmapname = string.Empty;
                    int tildeindex = openFileDialog2.FileName.LastIndexOf("~");
                    bool symbolfound = false;
                    if (tildeindex > 0)
                    {
                        tildeindex++;
                        mapname = openFileDialog2.FileName.Substring(tildeindex, openFileDialog2.FileName.Length - tildeindex);
                        mapname = mapname.Replace(".xls", "");
                        mapname = mapname.Replace(".XLS", "");
                        mapname = mapname.Replace(".Xls", "");
                       
                        // look if it is a valid symbolname
                        foreach (SymbolHelper sh in Tools.Instance.m_symbols)
                        {
                            if (sh.Varname.Replace(",", "").Replace("[","").Replace("]","") == mapname || sh.Userdescription.Replace(",", "") == mapname)
                            {
                                symbolfound = true;
                                realmapname = sh.Varname;
                                if (MessageBox.Show("Found valid symbol for import: " + sh.Varname + ". Are you sure you want to overwrite the map in the binary?", "Confirmation", MessageBoxButtons.YesNo) == DialogResult.Yes)
                                {
                                    // ok, overwrite info in binary
                                }
                                else
                                {
                                    mapname = string.Empty; // do nothing
                                    realmapname = string.Empty;
                                }
                            }
                        }
                        if (!symbolfound)
                        {
                            // ask user for symbol designation
                            frmSymbolSelect frmselect = new frmSymbolSelect(Tools.Instance.m_symbols);
                            if (frmselect.ShowDialog() == DialogResult.OK)
                            {
                                mapname = frmselect.SelectedSymbol;
                                realmapname = frmselect.SelectedSymbol;
                            }
                        }

                    }
                    else
                    {
                        // ask user for symbol designation
                        frmSymbolSelect frmselect = new frmSymbolSelect(Tools.Instance.m_symbols);
                        if (frmselect.ShowDialog() == DialogResult.OK)
                        {
                            mapname = frmselect.SelectedSymbol;
                            realmapname = frmselect.SelectedSymbol;
                        }

                    }
                    if (realmapname != string.Empty)
                    {
                        ImportExcelSymbol(realmapname, openFileDialog2.FileName);
                    }

                }
                catch (Exception E)
                {
                    frmInfoBox info = new frmInfoBox("Failed to import map from excel: " + E.Message);
                }
            }
        }

        private void ImportExcelSymbol(string symbolname, string filename)
        {
            ExcelInterface excelInterface = new ExcelInterface();
            bool issixteenbit = true;
            System.Data.DataTable dt = excelInterface.getDataFromXLS(filename);
            int symbollength = GetSymbolLength(Tools.Instance.m_symbols, symbolname);
            int datalength = symbollength;
            if (issixteenbit) datalength /= 2;
            int[] buffer = new int[datalength];
            int bcount = 0;
            //            for (int rtel = 1; rtel < dt.Rows.Count; rtel++)
            for (int rtel = dt.Rows.Count; rtel >= 1; rtel--)
            {
                try
                {
                    int idx = 0;
                    foreach (object o in dt.Rows[rtel].ItemArray)
                    {
                        if (idx > 0)
                        {
                            if (o != null)
                            {
                                if (o != DBNull.Value)
                                {
                                    if (bcount < buffer.Length)
                                    {
                                        buffer.SetValue(Convert.ToInt32(o), bcount++);
                                    }
                                    else
                                    {
                                        frmInfoBox info = new frmInfoBox("Too much information in file, abort");
                                        return;
                                    }
                                }
                            }
                        }
                        idx++;
                    }
                }
                catch (Exception E)
                {
                    Console.WriteLine("ImportExcelSymbol: " + E.Message);
                }

            }
            if (bcount >= datalength)
            {
                byte[] data = new byte[symbollength];
                int cellcount = 0;
                if (issixteenbit)
                {
                    for (int dcnt = 0; dcnt < buffer.Length; dcnt++)
                    {
                        string bstr1 = "0";
                        string bstr2 = "0";
                        int cellvalue = Convert.ToInt32(buffer.GetValue(dcnt));
                        string svalue = cellvalue.ToString("X4");

                        bstr1 = svalue.Substring(svalue.Length - 4, 2);
                        bstr2 = svalue.Substring(svalue.Length - 2, 2);
                        data.SetValue(Convert.ToByte(bstr1, 16), cellcount++);
                        data.SetValue(Convert.ToByte(bstr2, 16), cellcount++);
                    }
                }
                else
                {
                    for (int dcnt = 0; dcnt < buffer.Length; dcnt++)
                    {
                        int cellvalue = Convert.ToInt32(buffer.GetValue(dcnt));
                        data.SetValue(Convert.ToByte(cellvalue.ToString()), cellcount++);
                    }
                }
                Tools.Instance.savedatatobinary((int)GetSymbolAddress(Tools.Instance.m_symbols, symbolname), symbollength, data, Tools.Instance.m_currentfile, true, Tools.Instance.m_currentFileType);
                Tools.Instance.UpdateChecksum(Tools.Instance.m_currentfile, false);
            }


        }

        private void StartExcelExport()
        {
            ExcelInterface excelInterface = new ExcelInterface();
            if (gridViewSymbols.SelectedRowsCount > 0)
            {
                int[] selrows = gridViewSymbols.GetSelectedRows();
                if (selrows.Length > 0)
                {
                    SymbolHelper sh = (SymbolHelper)gridViewSymbols.GetRow((int)selrows.GetValue(0));
                    //DataRowView dr = (DataRowView)gridViewSymbols.GetRow((int)selrows.GetValue(0));
                    //frmTableDetail tabdet = new frmTableDetail();
                    string Map_name = sh.Varname;
                    if ((Map_name.StartsWith("2D") || Map_name.StartsWith("3D")) && sh.Userdescription != "") Map_name = sh.Userdescription;
                    int columns = 8;
                    int rows = 8;
                    int tablewidth = GetTableMatrixWitdhByName(Tools.Instance.m_currentfile, Tools.Instance.m_symbols, Map_name, out columns, out rows);

                    int address = (int)sh.Flash_start_address;
                    if (address != 0)
                    {
                        int length = sh.Length;

                        byte[] mapdata = Tools.Instance.readdatafromfile(Tools.Instance.m_currentfile, address, length, Tools.Instance.m_currentFileType);
                        int[] xaxis = GetXaxisValues(Tools.Instance.m_currentfile, Tools.Instance.m_symbols, Map_name);
                        int[] yaxis = GetYaxisValues(Tools.Instance.m_currentfile, Tools.Instance.m_symbols, Map_name);
                        Map_name = Map_name.Replace(",", "");
                        Map_name = Map_name.Replace("[", "");
                        Map_name = Map_name.Replace("]", "");

                        excelInterface.ExportToExcel(Map_name, address, length, mapdata, columns, rows, true, xaxis, yaxis, m_appSettings.ShowTablesUpsideDown, sh.X_axis_descr, sh.Y_axis_descr, sh.Z_axis_descr);
                    }
                }
            }
            else
            {
                frmInfoBox info = new frmInfoBox("No symbol selected in the primary symbol list");
            }
        }

        private void btnExportToExcel_ItemClick(object sender, ItemClickEventArgs e)
        {
            StartExcelExport();
        }

        private void btnExcelImport_ItemClick(object sender, ItemClickEventArgs e)
        {
            ImportFileInExcelFormat();
        }

        private void exportToExcelToolStripMenuItem_Click(object sender, EventArgs e)
        {
            StartExcelExport();
        }

        private void btnIQByMap_ItemClick(object sender, ItemClickEventArgs e)
        {
            StartTableViewer("IQ by MAP", 2);
        }

        private void btnIQByMAF_ItemClick(object sender, ItemClickEventArgs e)
        {
            StartTableViewer("IQ by MAF", 2);
        }

        private void btnSOILimiter_ItemClick(object sender, ItemClickEventArgs e)
        {
            StartTableViewer("SOI limiter", 2);
            
        }

        private void btnStartOfInjection_ItemClick(object sender, ItemClickEventArgs e)
        {
            StartTableViewer("Start of injection", 2);
        }

        private void btnInjectorDuration_ItemClick(object sender, ItemClickEventArgs e)
        {
            StartTableViewer("Injector duration", 2);
        }

        private void btnStartIQ_ItemClick(object sender, ItemClickEventArgs e)
        {
            StartTableViewer("Start IQ", 2);
        }

        //private void btnDisableFAP_ItemClick(object sender, ItemClickEventArgs e)
        //{
        //    // Demande de confirmation avant de désactiver le FAP
        //    var dialogResult = MessageBox.Show("Êtes-vous sûr de vouloir désactiver le FAP ?", "Confirmation", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        //    if (dialogResult == DialogResult.Yes)
        //    {
        //        MessageBox.Show("Désactivation du FAP en cours...");
        //        ModifyBinFile("disable_fap");
        //        MessageBox.Show("FAP désactivé !");
        //    }
        //}
        // Gestionnaire d'événement pour le bouton FAP
        private void btnDisableFAP_ItemClick(object sender, ItemClickEventArgs e)
        {
            string action = isFAPEnabled ? "désactiver" : "activer";

            // Message d'avertissement légal spécifique pour la désactivation du FAP
            if (isFAPEnabled)
            {
                string warningMessage = "AVERTISSEMENT LÉGAL :\n\n" +
                    "La désactivation du Filtre à Particules (FAP) :\n\n" +
                    "1. Est contraire à la réglementation environnementale en vigueur\n" +
                    "2. Peut entraîner l'échec du contrôle technique du véhicule\n" +
                    "3. Augmente significativement les émissions polluantes\n" +
                    "4. Peut entraîner des amendes et des poursuites légales\n" +
                    "5. Peut causer une augmentation de la consommation de carburant\n" +
                    "6. Annule la garantie constructeur sur le système d'échappement\n\n" +
                    "Cette modification est destinée UNIQUEMENT aux véhicules utilisés en compétition ou sur circuit fermé, non soumis au code de la route.\n\n" +
                    "Êtes-vous sûr de vouloir continuer ?";

                var warningResult = MessageBox.Show(warningMessage, "AVERTISSEMENT LÉGAL", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

                if (warningResult != DialogResult.Yes)
                {
                    return; // Annulation si l'utilisateur n'accepte pas l'avertissement
                }
            }

            // Message de confirmation standard
            string confirmMessage = $"Êtes-vous sûr de vouloir {action} le FAP ?";
            var dialogResult = MessageBox.Show(confirmMessage, "Confirmation", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

            if (dialogResult == DialogResult.Yes)
            {
                MessageBox.Show($"{(isFAPEnabled ? "Désactivation" : "Activation")} du FAP en cours...");
                ModifyFAPSetting();
                MessageBox.Show($"FAP {(isFAPEnabled ? "désactivé" : "activé")} !");

                // Inverser l'état du FAP après la modification
                isFAPEnabled = !isFAPEnabled;

                // Mettre à jour le texte du bouton
                UpdateButtonStates();
            }
        }

        //private void btnToggleStartStop_Click(object sender, ItemClickEventArgs e)
        //{
        //    // Demande de confirmation avant de modifier Start & Stop
        //    var dialogResult = MessageBox.Show("Voulez-vous modifier le paramètre Start & Stop ?", "Confirmation", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        //    if (dialogResult == DialogResult.Yes)
        //    {
        //        MessageBox.Show("Modification du Start & Stop...");
        //        ModifyBinFile("toggle_start_stop");
        //        MessageBox.Show("Start & Stop modifié !");
        //    }
        //}
        // Gestionnaire d'événement pour le bouton Start & Stop
        private void btnToggleStartStop_Click(object sender, ItemClickEventArgs e)
        {
            string action = isStartStopEnabled ? "désactiver" : "activer";
            string message = $"Êtes-vous sûr de vouloir {action} le Start & Stop ?";

            var dialogResult = MessageBox.Show(message, "Confirmation", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (dialogResult == DialogResult.Yes)
            {
                MessageBox.Show($"{(isStartStopEnabled ? "Désactivation" : "Activation")} du Start & Stop en cours...");
                ModifyStartStopSetting();
                MessageBox.Show($"Start & Stop {(isStartStopEnabled ? "désactivé" : "activé")} !");

                // Inverser l'état du Start & Stop après la modification
                isStartStopEnabled = !isStartStopEnabled;

                // Mettre à jour le texte du bouton
                UpdateButtonStates();
            }
        }
        // Fonction qui modifie le fichier BIN
        private void ModifyBinFile(string action)
        {
            if (string.IsNullOrEmpty(currentBinFilePath))
            {
                MessageBox.Show("Aucun fichier BIN chargé !", "Erreur", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            try
            {
                byte[] fileBytes = File.ReadAllBytes(currentBinFilePath);

                // Désactivation du FAP
                if (action == "disable_fap")
                {
                    int address = 0x123456; // Remplacer par l'adresse exacte pour désactiver le FAP
                    if (address < fileBytes.Length)
                    {
                        fileBytes[address] = 0x00; // Valeur pour désactiver le FAP
                    }
                    else
                    {
                        MessageBox.Show("Adresse mémoire invalide pour désactiver le FAP.", "Erreur", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }
                }
                // Modification du Start & Stop
                else if (action == "toggle_start_stop")
                {
                    int address = 0x654321; // Remplacer par l'adresse exacte pour Start/Stop
                    if (address < fileBytes.Length)
                    {
                        fileBytes[address] = (fileBytes[address] == 0x00) ? (byte)0x01 : (byte)0x00;
                    }
                    else
                    {
                        MessageBox.Show("Adresse mémoire invalide pour Start/Stop.", "Erreur", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }
                }

                // Enregistrer les modifications dans le fichier
                File.WriteAllBytes(currentBinFilePath, fileBytes);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Une erreur est survenue lors de la modification du fichier : " + ex.Message, "Erreur", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // Fonction qui modifie le paramètre FAP dans le fichier BIN
        private void ModifyFAPSetting()
        {
            if (string.IsNullOrEmpty(currentBinFilePath))
            {
                MessageBox.Show("Aucun fichier BIN chargé !", "Erreur", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            try
            {
                byte[] fileBytes = File.ReadAllBytes(currentBinFilePath);

                // Adresse du FAP
                int fapAddress = 0x123456; // À remplacer par l'adresse réelle

                if (fapAddress < fileBytes.Length)
                {
                    // Changer la valeur en fonction de l'état actuel (0x01 pour activer, 0x00 pour désactiver)
                    fileBytes[fapAddress] = isFAPEnabled ? (byte)0x00 : (byte)0x01;

                    // Enregistrer les modifications
                    File.WriteAllBytes(currentBinFilePath, fileBytes);
                }
                else
                {
                    MessageBox.Show("Adresse mémoire invalide pour le FAP.", "Erreur", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Une erreur est survenue lors de la modification du fichier : " + ex.Message, "Erreur", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // Fonction qui modifie le paramètre Start & Stop dans le fichier BIN
        private void ModifyStartStopSetting()
        {
            if (string.IsNullOrEmpty(currentBinFilePath))
            {
                MessageBox.Show("Aucun fichier BIN chargé !", "Erreur", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            try
            {
                byte[] fileBytes = File.ReadAllBytes(currentBinFilePath);

                // Adresse du Start & Stop
                int startStopAddress = 0x654321; // À remplacer par l'adresse réelle

                if (startStopAddress < fileBytes.Length)
                {
                    // Changer la valeur en fonction de l'état actuel (0x01 pour activer, 0x00 pour désactiver)
                    fileBytes[startStopAddress] = isStartStopEnabled ? (byte)0x00 : (byte)0x01;

                    // Enregistrer les modifications
                    File.WriteAllBytes(currentBinFilePath, fileBytes);
                }
                else
                {
                    MessageBox.Show("Adresse mémoire invalide pour Start & Stop.", "Erreur", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Une erreur est survenue lors de la modification du fichier : " + ex.Message, "Erreur", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        // Détecte si le véhicule a un FAP et s'il est activé
        private void DetectFAPStatus(byte[] fileBytes)
        {
            try
            {
                // Obtenir l'adresse du FAP pour cet ECU
                int fapAddress = GetMapAddress("FAP");

                if (fapAddress == 0)
                {
                    // Essayer d'autres noms possibles
                    fapAddress = GetMapAddress("DPF");
                    if (fapAddress == 0)
                        fapAddress = GetMapAddress("Particulate Filter");
                }

                // Si toujours pas d'adresse, utiliser l'adresse par défaut
                if (fapAddress == 0)
                {
                    // Adresse par défaut selon la marque
                    if (_currentECUType.Contains("BMW"))
                        fapAddress = 0x23F180;
                    else if (_currentECUType.Contains("AUDI"))
                        fapAddress = 0x158462;
                    else
                        fapAddress = 0x123456;
                }

                // Vérifier si l'adresse est valide
                if (fapAddress < fileBytes.Length)
                {
                    // Vérifier la valeur à cette adresse
                    isFAPEnabled = (fileBytes[fapAddress] == 0x01);
                    btnDisableFAP.Enabled = true;
                }
                else
                {
                    btnDisableFAP.Enabled = false;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Erreur lors de la détection du FAP : " + ex.Message);
                btnDisableFAP.Enabled = false;
            }
        }

        private void DetectStartStopStatus(byte[] fileBytes)
        {
            try
            {
                // Obtenir l'adresse du Start/Stop pour cet ECU
                int startStopAddress = GetMapAddress("StartStop");

                if (startStopAddress == 0)
                {
                    // Essayer d'autres noms possibles
                    startStopAddress = GetMapAddress("MSA");
                    if (startStopAddress == 0)
                        startStopAddress = GetMapAddress("Auto Start Stop");
                }

                // Si toujours pas d'adresse, utiliser l'adresse par défaut
                if (startStopAddress == 0)
                {
                    // Adresse par défaut selon la marque
                    if (_currentECUType.Contains("BMW"))
                        startStopAddress = 0x18E240;
                    else if (_currentECUType.Contains("AUDI"))
                        startStopAddress = 0x1A2468;
                    else
                        startStopAddress = 0x654321;
                }

                // Vérifier si l'adresse est valide
                if (startStopAddress < fileBytes.Length)
                {
                    // Vérifier la valeur à cette adresse
                    isStartStopEnabled = (fileBytes[startStopAddress] == 0x01);
                    btnToggleStartStop.Enabled = true;
                }
                else
                {
                    btnToggleStartStop.Enabled = false;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Erreur lors de la détection du Start/Stop : " + ex.Message);
                btnToggleStartStop.Enabled = false;
            }
        }

        // Met à jour l'état des boutons en fonction des détections
        private void UpdateButtonStates()
        {
            // Mettre à jour le texte du bouton FAP
            btnDisableFAP.Caption = isFAPEnabled ? "Désactiver FAP" : "Activer FAP";
            btnDisableFAP.Enabled = isFileLoaded;

            // Mettre à jour le texte du bouton Start & Stop
            btnToggleStartStop.Caption = isStartStopEnabled ? "Désactiver Start & Stop" : "Activer Start & Stop";
            btnToggleStartStop.Enabled = isFileLoaded;
        }

        public class CreatedMap
        {
            public string SymbolName { get; set; }
            public int FlashAddress { get; set; }
            public int Length { get; set; }
            public int XAxisLength { get; set; }
            public int YAxisLength { get; set; }
            public int XAxisAddress { get; set; }
            public int YAxisAddress { get; set; }
            public string XAxisDescription { get; set; }
            public string YAxisDescription { get; set; }
            public string ZAxisDescription { get; set; }
            public double Correction { get; set; }
            public double Offset { get; set; }
            public string Category { get; set; }
            public string Subcategory { get; set; }
            public string XAxisUnits { get; set; }
            public string YAxisUnits { get; set; }

            // Convertir un symbole existant en CreatedMap
            public static CreatedMap FromSymbol(SymbolHelper symbol)
            {
                return new CreatedMap
                {
                    SymbolName = symbol.Varname,
                    FlashAddress = (int)symbol.Flash_start_address,
                    Length = symbol.Length,
                    XAxisLength = symbol.X_axis_length,
                    YAxisLength = symbol.Y_axis_length,
                    XAxisAddress = symbol.X_axis_address,
                    YAxisAddress = symbol.Y_axis_address,
                    XAxisDescription = symbol.X_axis_descr,
                    YAxisDescription = symbol.Y_axis_descr,
                    ZAxisDescription = symbol.Z_axis_descr,
                    Correction = symbol.Correction,
                    Offset = symbol.Offset,
                    Category = symbol.Category,
                    Subcategory = symbol.Subcategory,
                    XAxisUnits = symbol.XaxisUnits,
                    YAxisUnits = symbol.YaxisUnits
                };
            }

            // Convertir cette CreatedMap en SymbolHelper
            public SymbolHelper ToSymbol()
            {
                SymbolHelper symbol = new SymbolHelper();
                symbol.Varname = this.SymbolName;
                symbol.Flash_start_address = this.FlashAddress;
                symbol.Length = this.Length;
                symbol.X_axis_length = this.XAxisLength;
                symbol.Y_axis_length = this.YAxisLength;
                symbol.X_axis_address = this.XAxisAddress;
                symbol.Y_axis_address = this.YAxisAddress;
                symbol.X_axis_descr = this.XAxisDescription;
                symbol.Y_axis_descr = this.YAxisDescription;
                symbol.Z_axis_descr = this.ZAxisDescription;
                symbol.Correction = this.Correction;
                symbol.Offset = this.Offset;
                symbol.Category = this.Category;
                symbol.Subcategory = this.Subcategory;
                symbol.XaxisUnits = this.XAxisUnits;
                symbol.YaxisUnits = this.YAxisUnits;
                return symbol;
            }
        }

        // Méthode pour sauvegarder les cartes créées
        private void SaveCreatedMaps(string fileName)
        {
            try
            {
                // Récupérer toutes les cartes créées
                List<CreatedMap> createdMaps = new List<CreatedMap>();

                foreach (SymbolHelper symbol in Tools.Instance.m_symbols)
                {
                    if (symbol.Varname.Contains("(créé)"))
                    {
                        createdMaps.Add(CreatedMap.FromSymbol(symbol));
                    }
                }

                // Sauvegarder dans un fichier JSON adjacent au fichier ECU
                string metadataFile = Path.Combine(
                    Path.GetDirectoryName(fileName),
                    Path.GetFileNameWithoutExtension(fileName) + ".createdmaps"
                );

                // Utiliser BinaryFormatter pour une sérialisation robuste
                using (FileStream fs = new FileStream(metadataFile, FileMode.Create))
                {
                    BinaryFormatter formatter = new BinaryFormatter();
                    formatter.Serialize(fs, createdMaps);
                }

                Console.WriteLine("Sauvegarde de " + createdMaps.Count + " cartes créées dans " + metadataFile);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Erreur lors de la sauvegarde des cartes créées: " + ex.Message);
            }
        }

        // Méthode pour charger les cartes créées
        private void LoadCreatedMaps(string fileName)
        {
            try
            {
                string metadataFile = Path.Combine(
                    Path.GetDirectoryName(fileName),
                    Path.GetFileNameWithoutExtension(fileName) + ".createdmaps"
                );

                if (File.Exists(metadataFile))
                {
                    List<CreatedMap> createdMaps;

                    using (FileStream fs = new FileStream(metadataFile, FileMode.Open))
                    {
                        BinaryFormatter formatter = new BinaryFormatter();
                        createdMaps = (List<CreatedMap>)formatter.Deserialize(fs);
                    }

                    // Ajouter les cartes à la collection de symboles
                    foreach (CreatedMap map in createdMaps)
                    {
                        // Vérifier si la carte existe déjà pour éviter les doublons
                        bool exists = false;
                        foreach (SymbolHelper symbol in Tools.Instance.m_symbols)
                        {
                            if (symbol.Flash_start_address == map.FlashAddress)
                            {
                                exists = true;
                                break;
                            }
                        }

                        if (!exists)
                        {
                            Tools.Instance.m_symbols.Add(map.ToSymbol());
                        }
                    }

                    Console.WriteLine("Chargement de " + createdMaps.Count + " cartes créées depuis " + metadataFile);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Erreur lors du chargement des cartes créées: " + ex.Message);
            }
        }

        private struct BinarySymbolRecord
        {
            public const int RECORD_SIZE = 64; // Taille fixe pour chaque enregistrement

            public uint Signature;      // 4 octets: signature "CSYM"
            public uint FlashAddress;   // 4 octets: adresse dans le fichier
            public ushort Length;       // 2 octets: longueur totale
            public ushort RowCount;     // 2 octets: nombre de lignes
            public ushort ColumnCount;  // 2 octets: nombre de colonnes
            public uint XAxisAddress;   // 4 octets: adresse de l'axe X
            public uint YAxisAddress;   // 4 octets: adresse de l'axe Y
            public float Correction;    // 4 octets: facteur de correction
            public float Offset;        // 4 octets: offset
                                        // Les autres 34 octets peuvent contenir le nom et les descriptions en format compressé
        }

        private int FindOrCreateSymbolSection(string fileName)
        {
            byte[] fileData = File.ReadAllBytes(fileName);

            // Rechercher la signature de la section des symboles créés
            byte[] signature = new byte[] { 0x43, 0x53, 0x59, 0x4D, 0x53, 0x45, 0x43, 0x54 }; // "CSYMSECT"

            for (int i = 0; i < fileData.Length - signature.Length; i++)
            {
                bool found = true;
                for (int j = 0; j < signature.Length; j++)
                {
                    if (fileData[i + j] != signature[j])
                    {
                        found = false;
                        break;
                    }
                }

                if (found)
                {
                    // Nous avons trouvé la section
                    return i + signature.Length;
                }
            }

            // Aucune section trouvée, créer une nouvelle section à la fin du fichier
            int endOfFile = fileData.Length;

            // Ajouter 8192 octets pour notre section de symboles (environ 128 symboles)
            byte[] newSection = new byte[8192 + signature.Length];

            // Copier la signature
            Array.Copy(signature, 0, newSection, 0, signature.Length);

            // Tout le reste est initialisé à 0xFF (espace libre)
            for (int i = signature.Length; i < newSection.Length; i++)
            {
                newSection[i] = 0xFF;
            }

            // Append to the end of the file
            using (FileStream fs = new FileStream(fileName, FileMode.Open, FileAccess.Write))
            {
                fs.Seek(endOfFile, SeekOrigin.Begin);
                fs.Write(newSection, 0, newSection.Length);
            }

            return endOfFile + signature.Length;
        }
        private void SaveSymbolToBinaryFile(SymbolHelper symbol, string fileName)
        {
            // Trouver la section des symboles
            int sectionStart = FindOrCreateSymbolSection(fileName);

            // Chercher un emplacement libre dans la section
            byte[] fileData = File.ReadAllBytes(fileName);
            int recordPosition = -1;

            // Vérifier si ce symbole existe déjà (par son adresse)
            for (int pos = sectionStart; pos < sectionStart + 8192; pos += BinarySymbolRecord.RECORD_SIZE)
            {
                // Lire la signature et l'adresse
                uint recordSignature = BitConverter.ToUInt32(fileData, pos);
                uint recordAddress = BitConverter.ToUInt32(fileData, pos + 4);

                if (recordSignature == 0x4D595343) // "CSYM"
                {
                    if (recordAddress == symbol.Flash_start_address)
                    {
                        // Ce symbole existe déjà, mettre à jour sa position
                        recordPosition = pos;
                        break;
                    }
                }
                else if (recordSignature == 0xFFFFFFFF || recordSignature == 0)
                {
                    // Emplacement libre trouvé
                    if (recordPosition == -1)
                        recordPosition = pos;
                }
            }

            if (recordPosition == -1)
            {
                // Pas d'emplacement libre trouvé
                throw new Exception("Aucun emplacement libre trouvé pour enregistrer le symbole");
            }

            // Créer l'enregistrement binaire
            byte[] recordData = new byte[BinarySymbolRecord.RECORD_SIZE];

            // Signature "CSYM"
            BitConverter.GetBytes((uint)0x4D595343).CopyTo(recordData, 0);

            // Adresse du symbole
            BitConverter.GetBytes((uint)symbol.Flash_start_address).CopyTo(recordData, 4);

            // Longueur
            BitConverter.GetBytes((ushort)symbol.Length).CopyTo(recordData, 8);

            // Dimensions
            BitConverter.GetBytes((ushort)symbol.X_axis_length).CopyTo(recordData, 10);
            BitConverter.GetBytes((ushort)symbol.Y_axis_length).CopyTo(recordData, 12);

            // Adresses des axes
            BitConverter.GetBytes((uint)symbol.X_axis_address).CopyTo(recordData, 14);
            BitConverter.GetBytes((uint)symbol.Y_axis_address).CopyTo(recordData, 18);

            // Facteurs de correction
            BitConverter.GetBytes((float)symbol.Correction).CopyTo(recordData, 22);
            BitConverter.GetBytes((float)symbol.Offset).CopyTo(recordData, 26);

            // Nom du symbole (compressé)
            byte[] nameBytes = Encoding.ASCII.GetBytes(symbol.Varname);
            int maxNameBytes = Math.Min(nameBytes.Length, 20);
            Array.Copy(nameBytes, 0, recordData, 30, maxNameBytes);

            // Écrire l'enregistrement dans le fichier
            using (FileStream fs = new FileStream(fileName, FileMode.Open, FileAccess.Write))
            {
                fs.Seek(recordPosition, SeekOrigin.Begin);
                fs.Write(recordData, 0, recordData.Length);
            }
        }
        private void LoadSymbolsFromBinaryFile(string fileName, SymbolCollection symbols)
        {
            try
            {
                byte[] fileData = File.ReadAllBytes(fileName);

                // Rechercher la signature de la section
                byte[] signature = new byte[] { 0x43, 0x53, 0x59, 0x4D, 0x53, 0x45, 0x43, 0x54 }; // "CSYMSECT"
                int sectionStart = -1;

                for (int i = 0; i < fileData.Length - signature.Length; i++)
                {
                    bool found = true;
                    for (int j = 0; j < signature.Length; j++)
                    {
                        if (fileData[i + j] != signature[j])
                        {
                            found = false;
                            break;
                        }
                    }

                    if (found)
                    {
                        sectionStart = i + signature.Length;
                        break;
                    }
                }

                if (sectionStart == -1)
                {
                    // Pas de section trouvée
                    return;
                }

                // Parcourir les enregistrements
                for (int pos = sectionStart; pos < Math.Min(sectionStart + 8192, fileData.Length - BinarySymbolRecord.RECORD_SIZE); pos += BinarySymbolRecord.RECORD_SIZE)
                {
                    // Lire la signature
                    uint recordSignature = BitConverter.ToUInt32(fileData, pos);

                    if (recordSignature == 0x4D595343) // "CSYM"
                    {
                        // Créer un nouveau symbole
                        SymbolHelper symbol = new SymbolHelper();

                        // Récupérer les informations
                        symbol.Flash_start_address = BitConverter.ToUInt32(fileData, pos + 4);
                        symbol.Length = BitConverter.ToUInt16(fileData, pos + 8);
                        symbol.X_axis_length = BitConverter.ToUInt16(fileData, pos + 10);
                        symbol.Y_axis_length = BitConverter.ToUInt16(fileData, pos + 12);
                        symbol.X_axis_address = (int)BitConverter.ToUInt32(fileData, pos + 14);
                        symbol.Y_axis_address = (int)BitConverter.ToUInt32(fileData, pos + 18);
                        symbol.Correction = BitConverter.ToSingle(fileData, pos + 22);
                        symbol.Offset = BitConverter.ToSingle(fileData, pos + 26);

                        // Récupérer le nom (rechercher le premier octet nul ou la fin des données)
                        int nameEnd = pos + 30;
                        while (nameEnd < pos + 50 && fileData[nameEnd] != 0)
                            nameEnd++;

                        symbol.Varname = Encoding.ASCII.GetString(fileData, pos + 30, nameEnd - (pos + 30));

                        // Si le nom est vide, générer un nom par défaut
                        if (string.IsNullOrEmpty(symbol.Varname))
                            symbol.Varname = "Carte créée à " + symbol.Flash_start_address.ToString("X8");

                        // Définir les descriptions des axes en fonction du nom
                        SetDefaultMapParameters(symbol, symbol.Varname.Replace(" (créé)", ""));

                        // Ajouter le symbole à la collection (éviter les doublons)
                        bool exists = false;
                        foreach (SymbolHelper existingSymbol in symbols)
                        {
                            if (existingSymbol.Flash_start_address == symbol.Flash_start_address)
                            {
                                exists = true;
                                break;
                            }
                        }

                        if (!exists)
                            symbols.Add(symbol);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Erreur lors du chargement des symboles depuis le fichier binaire: " + ex.Message);
            }
        }


    }
}
