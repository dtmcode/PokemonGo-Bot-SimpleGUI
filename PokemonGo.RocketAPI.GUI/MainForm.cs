using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using PokemonGo.RocketAPI.Enums;
using PokemonGo.RocketAPI.Logic;
using PokemonGo.RocketAPI.GeneratedCode;
using PokemonGo.RocketAPI.Extensions;
using PokemonGo.RocketAPI.Logic.Utils;
using System.IO;
using PokemonGo.RocketAPI.Exceptions;
using GMap.NET.MapProviders;

namespace PokemonGo.RocketAPI.GUI
{
    public partial class MainForm : Form
    {
        private Client _client;
        private Settings _settings;
        private Inventory _inventory;
        private GetPlayerResponse _profile;

        // Persisting Login Info
        private AuthType _loginMethod;
        private string _username;
        private string _password;

        // Create Console Window
        ConsoleForm console;

        private bool _isFarmingActive;

        public MainForm()
        {
            InitializeComponent();            
        }

        private void CleanUp()
        {
            // Clear Labels
            boxStatsExpHour.Clear();
            boxStatsPkmnTotal.Clear();
            boxStatsPkmnHour.Clear();
            boxStatsTimeElapsed.Clear();

            // Clear Labels
            boxLuckyEggsCount.Clear();
            boxIncencesCount.Clear();
            boxPokemonCount.Clear();
            boxInventoryCount.Clear();

            // Clear Experience
            _totalExperience = 0;
            _pokemonCaughtCount = 0;            
        }

        private void SetupLocationMap()
        {
            MainMap.DragButton = MouseButtons.Left;
            MainMap.MapProvider = GMapProviders.BingMap;
            MainMap.Position = new GMap.NET.PointLatLng(UserSettings.Default.DefaultLatitude, UserSettings.Default.DefaultLongitude);
            MainMap.MinZoom = 0;
            MainMap.MaxZoom = 24;
            MainMap.Zoom = 15;
        }

        private void UpdateMap(double lat, double lng)
        {
            MainMap.Position = new GMap.NET.PointLatLng(lat, lng);
        }

        private async void MainForm_Load(object sender, EventArgs e)
        {
            try
            {
                // Setup Console
                console = new ConsoleForm();
                console.TopMost = true;
                console.StartPosition = FormStartPosition.Manual;                
                console.Location = new System.Drawing.Point((Screen.PrimaryScreen.Bounds.Width / 2) - 530, (Screen.PrimaryScreen.Bounds.Height / 2) + 310);                

                // Start Loading
                StartLogger();
                CleanUp();

                // Begin Process
                await DisplayLoginWindow();                
                DisplayPositionSelector();                
                await GetCurrentPlayerInformation();
                await PreflightCheck();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
                Logger.Write(ex.Message);
            }
        }

        private void DisplayPositionSelector()
        {
            // Display Position Selector
            LocationSelector locationSelect = new LocationSelector();
            locationSelect.ShowDialog();

            // Check if Position was Selected
            try
            {
                if (!locationSelect.setPos)
                    throw new ArgumentException();

                // Persisting the Initial Position
                _client.SaveLatLng(locationSelect.lat, locationSelect.lng);
                _client.SetCoordinates(locationSelect.lat, locationSelect.lng, UserSettings.Default.DefaultAltitude);
            }
            catch
            {
                MessageBox.Show(@"You need to declare a valid starting location.", @"Safety Check");
                MessageBox.Show(@"To protect your account of a possible soft ban, the software will close.", @"Safety Check");
                Application.Exit();
            }

            // Display Starting Location
            Logger.Write($"Starting in Location Lat: {UserSettings.Default.DefaultLatitude} Lng: {UserSettings.Default.DefaultLongitude}");

            // Close the Location Window
            locationSelect.Close();

            // Setup MiniMap
            SetupLocationMap();
        }

        private async Task DisplayLoginWindow()
        {
            // Display Login
            Hide();
            LoginForm loginForm = new LoginForm();
            loginForm.ShowDialog();
            Show();

            // Display Console
            console.Show();

            // Check if an Option was Selected
            if (!loginForm.loginSelected)
                Application.Exit();

            // Determine Login Method
            if (loginForm.auth == AuthType.Ptc)
                await LoginPtc(loginForm.boxUsername.Text, loginForm.boxPassword.Text);
            if (loginForm.auth == AuthType.Google)
                await LoginGoogle(loginForm.boxUsername.Text, loginForm.boxPassword.Text);

            // Select the Location
            Logger.Write("Select Starting Location...");

            // Close the Login Form
            loginForm.Close();
        }

        private void StartLogger()
        {
            GUILogger guiLog = new GUILogger(LogLevel.Info);
            guiLog.setLoggingBox(console.boxConsole);
            Logger.SetLogger(guiLog);
        }

        private async Task LoginGoogle(string username, string password)
        {
            try
            {
                // Login Method
                _loginMethod = AuthType.Google;
                _username = username;
                _password = password;

                // Creating the Settings
                Logger.Write("Adjusting the Settings.");
                UserSettings.Default.AuthType = AuthType.Google.ToString();
                _settings = new Settings();

                // Begin Login
                Logger.Write("Trying to Login with Google Token...");
                Client client = new Client(_settings);
                client.DoGoogleLogin(username, password);
                await client.SetServer();

                // Server Ready
                Logger.Write("Connected! Server is Ready.");
                _client = client;

                Logger.Write("Attempting to Retrieve Inventory and Player Profile...");
                _inventory = new Inventory(client);
                _profile = await client.GetProfile();
                EnableButtons();
            }
            catch
            {
                Logger.Write("Unable to Connect using the Google Token.");
                MessageBox.Show(@"Unable to Authenticate with Login Server.", @"Login Problem");
                Application.Exit();
            }
        }

        private async Task LoginPtc(string username, string password)
        {
            try
            {
                // Login Method
                _loginMethod = AuthType.Ptc;
                _username = username;
                _password = password;

                // Creating the Settings
                Logger.Write("Adjusting the Settings.");
                UserSettings.Default.AuthType = AuthType.Ptc.ToString();
                UserSettings.Default.PtcUsername = username;
                UserSettings.Default.PtcPassword = password;
                _settings = new Settings();

                // Begin Login
                Logger.Write("Trying to Login with PTC Credentials...");
                Client client = new Client(_settings);
                await client.DoPtcLogin(_settings.PtcUsername, _settings.PtcPassword);
                await client.SetServer();

                // Server Ready
                Logger.Write("Connected! Server is Ready.");
                _client = client;

                Logger.Write("Attempting to Retrieve Inventory and Player Profile...");
                _inventory = new Inventory(client);
                _profile = await client.GetProfile();
                EnableButtons();
            }         
            catch
            {
                Logger.Write("Unable to Connect using the PTC Credentials.");
                MessageBox.Show(@"Unable to Authenticate with Login Server.", @"Login Problem");
                Application.Exit();
            }
        }

        private void EnableButtons()
        {
            startToolStripMenuItem.Enabled = true;
            transferDuplicatePokemonToolStripMenuItem.Enabled = true;
            recycleItemsToolStripMenuItem.Enabled = true;
            evolveAllPokemonwCandyToolStripMenuItem.Enabled = true;            
            myPokemonToolStripMenuItem.Enabled = true;

            Logger.Write("Ready to Work.");
        }

        private async Task<bool> PreflightCheck()
        {
            // Get Pokemons and Inventory
            var myItems = await _inventory.GetItems();
            var myPokemons = await _inventory.GetPokemons();
            
            // Write to Console
            var items = myItems as IList<Item> ?? myItems.ToList();
            var pokemon = myPokemons as IList<PokemonData> ?? myPokemons.ToList();

            Logger.Write($"Items in Bag: {items.Select(i => i.Count).Sum()}/350.");
            Logger.Write($"Lucky Eggs in Bag: {items.FirstOrDefault(p => (ItemId)p.Item_ == ItemId.ItemLuckyEgg)?.Count ?? 0 }");
            Logger.Write($"Pokemons in Bag: {pokemon.Count()}/250.");

            // Checker for Inventory
            if (items.Select(i => i.Count).Sum() >= 350)
            {
                Logger.Write("Unable to Start Farming: You need to have free space for Items.");
                return false;
            }

            // Checker for Pokemons
            if (pokemon.Count() >= 241) // Eggs are Included in the total count (9/9)
            {
                Logger.Write("Unable to Start Farming: You need to have free space for Pokemons.");
                return false;
            }

            // Ready to Fly
            Logger.Write("Inventory and Pokemon Space, Ready.");
            return true;
        }

        private void disableButtonsDuringFarming()
        {
            // Disable Button
            startToolStripMenuItem.Enabled = false;
            evolveAllPokemonwCandyToolStripMenuItem.Enabled = false;
            recycleItemsToolStripMenuItem.Enabled = false;
            transferDuplicatePokemonToolStripMenuItem.Enabled = false;
            viewMyPokemonsToolStripMenuItem.Enabled = false;

            stopToolStripMenuItem.Enabled = true;
        }

        ////////////////////////
        // EXP COUNTER MODULE //
        ////////////////////////

        private double _totalExperience;
        private int _pokemonCaughtCount;
        private int _pokestopsCount;
        private DateTime _sessionStartTime;
        private readonly Timer _sessionTimer = new Timer();

        private void SetUpTimer()
        {
            _sessionTimer.Tick += TimerTick;
            _sessionTimer.Enabled = true;
        }

        private void TimerTick(object sender, EventArgs e)
        {
            boxStatsTimeElapsed.Text = (DateTime.Now - _sessionStartTime).TotalSeconds.ToString("0") + " Sec.";
            boxStatsExpHour.Text = GetExpPerHour();
            boxStatsPkmnHour.Text = GetPokemonPerHour();
            boxStatsPkmnTotal.Text = _pokemonCaughtCount.ToString();
        }

        private string GetExpPerHour()
        {
            double expPerHour = (_totalExperience * 3600) / (DateTime.Now - _sessionStartTime).TotalSeconds;
            return $"{expPerHour:0.0}";
        }

        private string GetPokemonPerHour()
        {
            double pkmnPerHour = (_pokemonCaughtCount * 3600) / (DateTime.Now - _sessionStartTime).TotalSeconds;
            return $"{pkmnPerHour:0.0}";
        }

        private async void StartBottingSession()
        {
            // Setup the Timer
            _sessionTimer.Interval = 5000;
            _sessionTimer.Start();
            _sessionStartTime = DateTime.Now;

            // Loop Until we Manually Stop
            while(_isFarmingActive)
            {
                try
                {
                    // Start Farming Pokestops/Pokemons.
                    await ExecuteFarmingPokestopsAndPokemons();

                    // Only Auto-Evolve/Transfer when Continuous.
                    if (_isFarmingActive && GUISettings.Default.autoEvolveTransfer)
                    {
                        // Evolve Pokemons.
                        await EvolveAllPokemonWithEnoughCandy();

                        // Transfer Duplicates.
                        await TransferDuplicatePokemon();
                    }
                }
                catch (InvalidResponseException)
                {
                    Logger.Write("------------> InvalidReponseException");
                    Logger.Write("<------------ Recovering");

                    // Re-Authenticate with Server
                    switch (_loginMethod)
                    {
                        case AuthType.Ptc:
                            await LoginPtc(_username, _password);
                            break;

                        case AuthType.Google:
                            await LoginGoogle(_username, _password);
                            break;
                    }

                    // Disable Buttons
                    disableButtonsDuringFarming();
                }
                catch (Exception)
                {
                    Logger.Write("------------> GeneralException");
                    Logger.Write("<------------ Recovering");

                    // Re-Authenticate with Server
                    switch (_loginMethod)
                    {
                        case AuthType.Ptc:
                            await LoginPtc(_username, _password);
                            break;

                        case AuthType.Google:
                            await LoginGoogle(_username, _password);
                            break;
                    }

                    // Disable Buttons
                    disableButtonsDuringFarming();
                }
            }           
        }

        private void StopBottingSession()
        {
            _sessionTimer.Stop();

            boxPokestopName.Clear();
            boxPokestopInit.Clear();
            boxPokestopCount.Clear();

            MessageBox.Show(@"Please allow a few seconds for the pending tasks to complete.", "PoGo Bot");
        }

        ///////////////////////
        // API LOGIC MODULES //
        ///////////////////////
        
        public async Task GetCurrentPlayerInformation()
        {
            var playerName = _profile.Profile.Username ?? "";
            var playerStats = await _inventory.GetPlayerStats();
            var playerStat = playerStats.FirstOrDefault();
            if (playerStat != null)
            {
                var xpDifference = GetXpDiff(playerStat.Level);                
                lbName.Text = playerName;
                lbLevel.Text = $"Lv {playerStat.Level}";
                lbExperience.Text = $"{playerStat.Experience - playerStat.PrevLevelXp - xpDifference}/{playerStat.NextLevelXp - playerStat.PrevLevelXp - xpDifference} XP";

                expProgressBar.Minimum = 1;
                expProgressBar.Maximum = (int)(playerStat.NextLevelXp - playerStat.PrevLevelXp - xpDifference);
                expProgressBar.Value = (int)(playerStat.Experience - playerStat.PrevLevelXp - xpDifference);                
            }

            // Get Pokemons and Inventory
            var myItems = await _inventory.GetItems();
            var myPokemons = await _inventory.GetPokemons();

            // Write to Console
            var items = myItems as IList<Item> ?? myItems.ToList();
            boxInventoryCount.Text = $"{items.Select(i => i.Count).Sum()}/350";
            boxPokemonCount.Text = $"{myPokemons.Count()}/250";
            boxLuckyEggsCount.Text = (items.FirstOrDefault(p => (ItemId)p.Item_ == ItemId.ItemLuckyEgg)?.Count ?? 0).ToString();
            boxIncencesCount.Text = (items.FirstOrDefault(p => (ItemId)p.Item_ == ItemId.ItemIncenseOrdinary)?.Count ?? 0).ToString();            
        }

        public static int GetXpDiff(int level)
        {
            switch (level)
            {
                case 1:
                    return 0;
                case 2:
                    return 1000;
                case 3:
                    return 2000;
                case 4:
                    return 3000;
                case 5:
                    return 4000;
                case 6:
                    return 5000;
                case 7:
                    return 6000;
                case 8:
                    return 7000;
                case 9:
                    return 8000;
                case 10:
                    return 9000;
                case 11:
                    return 10000;
                case 12:
                    return 10000;
                case 13:
                    return 10000;
                case 14:
                    return 10000;
                case 15:
                    return 15000;
                case 16:
                    return 20000;
                case 17:
                    return 20000;
                case 18:
                    return 20000;
                case 19:
                    return 25000;
                case 20:
                    return 25000;
                case 21:
                    return 50000;
                case 22:
                    return 75000;
                case 23:
                    return 100000;
                case 24:
                    return 125000;
                case 25:
                    return 150000;
                case 26:
                    return 190000;
                case 27:
                    return 200000;
                case 28:
                    return 250000;
                case 29:
                    return 300000;
                case 30:
                    return 350000;
                case 31:
                    return 500000;
                case 32:
                    return 500000;
                case 33:
                    return 750000;
                case 34:
                    return 1000000;
                case 35:
                    return 1250000;
                case 36:
                    return 1500000;
                case 37:
                    return 2000000;
                case 38:
                    return 2500000;
                case 39:
                    return 1000000;
                case 40:
                    return 1000000;
            }
            return 0;
        }

        private async Task EvolveAllPokemonWithEnoughCandy()
        {
            // Clear Grid
            dGrid.Rows.Clear();

            // Prepare Grid
            dGrid.ColumnCount = 3;
            dGrid.Columns[0].Name = "Action";
            dGrid.Columns[1].Name = "Pokemon";
            dGrid.Columns[2].Name = "Experience";

            // Logging
            Logger.Write("Selecting Pokemons available for Evolution.");

            var pokemonToEvolve = await _inventory.GetPokemonToEvolve();
            foreach (var pokemon in pokemonToEvolve)
            {
                var evolvePokemonOutProto = await _client.EvolvePokemon(pokemon.Id); 

                if (evolvePokemonOutProto.Result == EvolvePokemonOut.Types.EvolvePokemonStatus.PokemonEvolvedSuccess)
                {
                    Logger.Write($"Evolved {pokemon.PokemonId} successfully for {evolvePokemonOutProto.ExpAwarded}xp");

                    // GUI Experience
                    _totalExperience += evolvePokemonOutProto.ExpAwarded;
                    dGrid.Rows.Insert(0, "Evolved", pokemon.PokemonId.ToString(), evolvePokemonOutProto.ExpAwarded);
                }                    
                else
                {
                    Logger.Write($"Failed to evolve {pokemon.PokemonId}. EvolvePokemonOutProto.Result was {evolvePokemonOutProto.Result}, stopping evolving {pokemon.PokemonId}");
                }

                await GetCurrentPlayerInformation();
                await Task.Delay(2000);
            }

            // Logging
            Logger.Write("Finished Evolving Pokemons.");
        }

        private async Task TransferDuplicatePokemon(bool keepPokemonsThatCanEvolve = false)
        {
            // Clear Grid
            dGrid.Rows.Clear();

            // Prepare Grid
            dGrid.ColumnCount = 3;
            dGrid.Columns[0].Name = "Action";
            dGrid.Columns[1].Name = "Pokemon";
            dGrid.Columns[2].Name = "CP";

            // Logging
            Logger.Write("Selecting Pokemons available for Transfer.");

            var duplicatePokemons = await _inventory.GetDuplicatePokemonToTransfer(keepPokemonsThatCanEvolve);

            foreach (var duplicatePokemon in duplicatePokemons)
            {
                var iv = Logic.Logic.CalculatePokemonPerfection(duplicatePokemon);
                if (iv < GUISettings.Default.minIV && duplicatePokemon.Cp < GUISettings.Default.minCP)
                {
                    var transfer = await _client.TransferPokemon(duplicatePokemon.Id);
                    Logger.Write($"Transfer {duplicatePokemon.PokemonId} with {duplicatePokemon.Cp} CP and an IV of { iv }");

                    // Add Row to DataGrid
                    dGrid.Rows.Insert(0, "Transferred", duplicatePokemon.PokemonId.ToString(), duplicatePokemon.Cp);

                    await GetCurrentPlayerInformation();
                    await Task.Delay(500);
                }
                else
                {
                    Logger.Write($"Will not transfer {duplicatePokemon.PokemonId} with {duplicatePokemon.Cp} CP and an IV of { iv }");
                    // Add Row to DataGrid
                    dGrid.Rows.Insert(0, "Not transferred", duplicatePokemon.PokemonId.ToString(), duplicatePokemon.Cp);
                }
            }

            // Logging
            Logger.Write("Finished Transfering Pokemons.");
        }

        private async Task RecycleItems()
        {   
            try
            {
                // Clear Grid
                dGrid.Rows.Clear();

                // Prepare Grid
                dGrid.ColumnCount = 3;
                dGrid.Columns[0].Name = "Action";
                dGrid.Columns[1].Name = "Count";
                dGrid.Columns[2].Name = "Item";

                // Logging
                Logger.Write("Recycling Items to Free Space");

                var items = await _inventory.GetItemsToRecycle(_settings);

                foreach (var item in items)
                {
                    var transfer = await _client.RecycleItem((ItemId)item.Item_, item.Count);
                    Logger.Write($"Recycled {item.Count}x {(ItemId)item.Item_}", LogLevel.Info);

                    // GUI Experience
                    dGrid.Rows.Insert(0, "Recycled", item.Count, ((ItemId)item.Item_).ToString());

                    await Task.Delay(500);
                }

                await GetCurrentPlayerInformation();

                // Logging
                Logger.Write("Recycling Complete.");
            }
            catch (Exception ex)
            {
                Logger.Write($"Error Details: {ex.Message}");
                Logger.Write("Unable to Complete Items Recycling.");
            }            
        }

        private async Task<MiscEnums.Item> GetBestBall(int? pokemonCp)
        {
            var pokeBallsCount = await _inventory.GetItemAmountByType(MiscEnums.Item.ITEM_POKE_BALL);
            var greatBallsCount = await _inventory.GetItemAmountByType(MiscEnums.Item.ITEM_GREAT_BALL);
            var ultraBallsCount = await _inventory.GetItemAmountByType(MiscEnums.Item.ITEM_ULTRA_BALL);
            var masterBallsCount = await _inventory.GetItemAmountByType(MiscEnums.Item.ITEM_MASTER_BALL);

            if (masterBallsCount > 0 && pokemonCp >= 1000)
                return MiscEnums.Item.ITEM_MASTER_BALL;
            else if (ultraBallsCount > 0 && pokemonCp >= 1000)
                return MiscEnums.Item.ITEM_ULTRA_BALL;
            else if (greatBallsCount > 0 && pokemonCp >= 1000)
                return MiscEnums.Item.ITEM_GREAT_BALL;

            if (ultraBallsCount > 0 && pokemonCp >= 600)
                return MiscEnums.Item.ITEM_ULTRA_BALL;
            else if (greatBallsCount > 0 && pokemonCp >= 600)
                return MiscEnums.Item.ITEM_GREAT_BALL;

            if (greatBallsCount > 0 && pokemonCp >= 350)
                return MiscEnums.Item.ITEM_GREAT_BALL;

            if (pokeBallsCount > 0)
                return MiscEnums.Item.ITEM_POKE_BALL;
            if (greatBallsCount > 0)
                return MiscEnums.Item.ITEM_GREAT_BALL;
            if (ultraBallsCount > 0)
                return MiscEnums.Item.ITEM_ULTRA_BALL;
            if (masterBallsCount > 0)
                return MiscEnums.Item.ITEM_MASTER_BALL;

            return MiscEnums.Item.ITEM_POKE_BALL;
        }

        public async Task UseBerry(ulong encounterId, string spawnPointId)
        {
            var inventoryItems = await _inventory.GetItems();
            var berries = inventoryItems.Where(p => (ItemId)p.Item_ == ItemId.ItemRazzBerry);
            var berry = berries.FirstOrDefault();

            if (berry == null)
                return;

            var useRaspberry = await _client.UseCaptureItem(encounterId, ItemId.ItemRazzBerry, spawnPointId); // Redundant?
            Logger.Write($"Used Rasperry. Remaining: {berry.Count}");
            await Task.Delay(3000);
        }

        public async Task UseLuckyEgg()
        {
            var inventoryItems = await _inventory.GetItems();
            var luckyEggs = inventoryItems.Where(p => (ItemId)p.Item_ == ItemId.ItemLuckyEgg);
            var luckyEgg = luckyEggs.FirstOrDefault();

            if (luckyEgg == null)
                return;
            
            var useLuckyEgg = await _client.UseItemExpBoost(ItemId.ItemLuckyEgg); // Redundant?
            Logger.Write($"Used LuckyEgg. Remaining: {luckyEgg.Count - 1}");

            await GetCurrentPlayerInformation();
        }

        public async Task UseIncense()
        {
            var inventoryItems = await _inventory.GetItems();
            var incenses = inventoryItems.Where(p => (ItemId)p.Item_ == ItemId.ItemIncenseOrdinary);
            var incense = incenses.FirstOrDefault();

            if (incense == null)
                return;

            var useIncense = await _client.UseItemIncense(ItemId.ItemIncenseOrdinary); // Redundant?
            Logger.Write($"Used Incense. Remaining: {incense.Count - 1}");

            await GetCurrentPlayerInformation();
        }

        Random r = new Random();

        private async Task ExecuteFarmingPokestopsAndPokemons()
        {
            var mapObjects = await _client.GetMapObjects();

            var pokeStops = mapObjects.MapCells.SelectMany(i => i.Forts).Where(i => i.Type == FortType.Checkpoint && i.CooldownCompleteTimestampMs < DateTime.UtcNow.ToUnixTime());

            var fortDatas = pokeStops as IList<FortData> ?? pokeStops.ToList();
            _pokestopsCount = fortDatas.Count<FortData>();
            int count = 1;

            foreach (var pokeStop in fortDatas)
            {
                await Task.Delay(2000);
                var update = await _client.UpdatePlayerLocation(pokeStop.Latitude, pokeStop.Longitude, _settings.DefaultAltitude); // Redundant?
                UpdateMap(pokeStop.Latitude, pokeStop.Longitude);
                await Task.Delay(2000);
                var fortInfo = await _client.GetFort(pokeStop.Id, pokeStop.Latitude, pokeStop.Longitude);

                boxPokestopName.Text = fortInfo.Name;
                boxPokestopInit.Text = count.ToString();
                boxPokestopCount.Text = _pokestopsCount.ToString();
                count++;

                await Task.Delay(2000);
                var fortSearch = await _client.SearchFort(pokeStop.Id, pokeStop.Latitude, pokeStop.Longitude);
                Logger.Write($"Loot -> Gems: { fortSearch.GemsAwarded}, Eggs: {fortSearch.PokemonDataEgg} Items: {StringUtils.GetSummedFriendlyNameOfItemAwardList(fortSearch.ItemsAwarded)}");
                Logger.Write("Gained " + fortSearch.ExperienceAwarded + " XP.");

                // Experience Counter
                _totalExperience += fortSearch.ExperienceAwarded;

                await GetCurrentPlayerInformation();
                await Task.Delay(2000);
                Logger.Write("Attempting to Capture Nearby Pokemons.");
                await ExecuteCatchAllNearbyPokemons();

                if (!_isFarmingActive)
                {
                    Logger.Write("Stopping Farming Pokestops.");
                    return;
                }                    

                Logger.Write("Waiting before moving to the next Pokestop.");
                await Task.Delay(GUISettings.Default.pokestopDelay * 2500);
            }
        }

        private async Task ExecuteCatchAllNearbyPokemons()
        {
            var mapObjects = await _client.GetMapObjects();
            await Task.Delay(2000);
            var pokemons = mapObjects.MapCells.SelectMany(i => i.CatchablePokemons);
            await Task.Delay(2000);
            var mapPokemons = pokemons as IList<MapPokemon> ?? pokemons.ToList();
            Logger.Write("Found " + mapPokemons.Count<MapPokemon>() + " Pokemons in the area.");
            foreach (var pokemon in mapPokemons)
            {
                await Task.Delay(2000);
                var update = await _client.UpdatePlayerLocation(pokemon.Latitude, pokemon.Longitude, _settings.DefaultAltitude); // Redundant?
                await Task.Delay(2000);
                var encounterPokemonResponse = await _client.EncounterPokemon(pokemon.EncounterId, pokemon.SpawnpointId);
                await Task.Delay(2000);
                var pokemonCp = encounterPokemonResponse?.WildPokemon?.PokemonData?.Cp;
                await Task.Delay(2000);
                var pokemonIv = Logic.Logic.CalculatePokemonPerfection(encounterPokemonResponse?.WildPokemon?.PokemonData).ToString("0.00") + "%";
                await Task.Delay(2000);
                var pokeball = await GetBestBall(pokemonCp);

                Logger.Write($"Fighting {pokemon.PokemonId} with Capture Probability of {(encounterPokemonResponse?.CaptureProbability.CaptureProbability_.First())*100:0.0}%");

                boxPokemonName.Text = pokemon.PokemonId.ToString();
                boxPokemonCaughtProb.Text = (encounterPokemonResponse?.CaptureProbability.CaptureProbability_.First() * 100).ToString() + @"%";                

                CatchPokemonResponse caughtPokemonResponse;
                do
                {
                    if (encounterPokemonResponse?.CaptureProbability.CaptureProbability_.First() < (GUISettings.Default.minBerry / 100))
                    {
                        await UseBerry(pokemon.EncounterId, pokemon.SpawnpointId);
                    }

                    caughtPokemonResponse = await _client.CatchPokemon(pokemon.EncounterId, pokemon.SpawnpointId, pokemon.Latitude, pokemon.Longitude, pokeball);
                    await Task.Delay(2000);
                }
                while (caughtPokemonResponse.Status == CatchPokemonResponse.Types.CatchStatus.CatchMissed);

                Logger.Write(caughtPokemonResponse.Status == CatchPokemonResponse.Types.CatchStatus.CatchSuccess ? $"We caught a {pokemon.PokemonId} with CP {encounterPokemonResponse?.WildPokemon?.PokemonData?.Cp} using a {pokeball}" : $"{pokemon.PokemonId} with CP {encounterPokemonResponse?.WildPokemon?.PokemonData?.Cp} got away while using a {pokeball}..");

                if (caughtPokemonResponse.Status == CatchPokemonResponse.Types.CatchStatus.CatchSuccess)
                {
                    // Calculate Experience
                    int fightExperience = 0;
                    foreach (int exp in caughtPokemonResponse.Scores.Xp)
                        fightExperience += exp;
                    _totalExperience += fightExperience;
                    Logger.Write("Gained " + fightExperience + " XP.");
                    _pokemonCaughtCount++;

                    // Add Row to the DataGrid
                    dGrid.Rows.Insert(0, "Captured", pokemon.PokemonId.ToString(), encounterPokemonResponse?.WildPokemon?.PokemonData?.Cp, pokemonIv);
                }
                else
                {
                    // Add Row to the DataGrid
                    dGrid.Rows.Insert(0, "Ran Away", pokemon.PokemonId.ToString(), encounterPokemonResponse?.WildPokemon?.PokemonData?.Cp, pokemonIv);
                }

                boxPokemonName.Clear();
                boxPokemonCaughtProb.Clear();

                await GetCurrentPlayerInformation();

                if (!_isFarmingActive)
                {
                    Logger.Write("Stopping Farming Pokemons.");
                    return;
                }

                Logger.Write("Waiting before moving to the next Pokemon.");
                await Task.Delay(GUISettings.Default.pokemonDelay * 1000);
            }
        }

        private void exitToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var confirm = MessageBox.Show("Do you want to close the bot?", "PoGo Bot", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (confirm == DialogResult.Yes)
                Application.Exit();
        }

        private async void showStatisticsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var stuff = await _inventory.GetPlayerStats();
            var stats = stuff.FirstOrDefault();
            if (stats != null)
                MessageBox.Show($"Battle Attack Won: {stats.BattleAttackTotal}\n" +
                                $"Battle Attack Total: {stats.BattleAttackTotal}\n" +
                                $"Battle Defended Won: {stats.BattleDefendedWon}\n" +
                                $"Battle Training Won: {stats.BattleTrainingWon}\n" +
                                $"Battle Training Total: {stats.BattleTrainingTotal}\n" +
                                $"Big Magikarp Caught: {stats.BigMagikarpCaught}\n" +
                                $"Eggs Hatched: {stats.EggsHatched}\n" +
                                $"Evolutions: {stats.Evolutions}\n" +
                                $"Km Walked: {stats.KmWalked}\n" +
                                $"Pokestops Visited: {stats.PokeStopVisits}\n" +
                                $"Pokeballs Thrown: {stats.PokeballsThrown}\n" +
                                $"Pokemon Deployed: {stats.PokemonDeployed}\n" +
                                $"Pokemon Captured: {stats.PokemonsCaptured}\n" +
                                $"Pokemon Encountered: {stats.PokemonsEncountered}\n" +
                                $"Prestige Dropped Total: {stats.PrestigeDroppedTotal}\n" +
                                $"Prestige Raised Total: {stats.PrestigeRaisedTotal}\n" +
                                $"Small Rattata Caught: {stats.SmallRattataCaught}\n" +
                                $"Unique Pokedex Entries: {stats.UniquePokedexEntries}", "PoGo Bot");
        }

        private async void startToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (!await PreflightCheck())
                return;

            // Disable Buttons
            disableButtonsDuringFarming();

            // Setup the Timer
            _isFarmingActive = true;
            SetUpTimer();
            StartBottingSession();

            // Clear Grid
            dGrid.Rows.Clear();

            // Prepare Grid
            dGrid.ColumnCount = 4;
            dGrid.Columns[0].Name = "Action";
            dGrid.Columns[1].Name = "Pokemon";
            dGrid.Columns[2].Name = "CP";
            dGrid.Columns[3].Name = "IV";
        }

        private void stopToolStripMenuItem_Click(object sender, EventArgs e)
        {
            // Disable Button
            startToolStripMenuItem.Enabled = true;
            transferDuplicatePokemonToolStripMenuItem.Enabled = true;
            recycleItemsToolStripMenuItem.Enabled = true;
            evolveAllPokemonwCandyToolStripMenuItem.Enabled = true;
            viewMyPokemonsToolStripMenuItem.Enabled = true;

            stopToolStripMenuItem.Enabled = false;

            // Close the Timer
            _isFarmingActive = false;
            StopBottingSession();
        }

        private void displayConsoleToolStripMenuItem_Click(object sender, EventArgs e)
        {
            console.Show();
        }

        private async void recycleItemsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            await RecycleItems();
        }

        private async void luckyEgg0ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            await UseLuckyEgg();
        }

        private async void incence0ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            await UseIncense();
        }

        private void viewMyPokemonsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var myPokemonsListForm = new PokemonForm(_client);
            myPokemonsListForm.ShowDialog();
        }

        private async void evolveAllPokemonwCandyToolStripMenuItem_Click(object sender, EventArgs e)
        {
            await EvolveAllPokemonWithEnoughCandy();
        }

        private async void transferDuplicatePokemonToolStripMenuItem_Click(object sender, EventArgs e)
        {
            await TransferDuplicatePokemon();
        }

        private void settingsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            GUISettingsForm settingsGUI = new GUISettingsForm();
            settingsGUI.ShowDialog();            
        }

        private void aboutToolStripMenuItem_Click(object sender, EventArgs e)
        {
            MessageBox.Show("PoGo Bot SimpleGUI is an Open Source Project Created by Jorge Limas." + Environment.NewLine + Environment.NewLine +
                "You can get the latest version for FREE at:" + Environment.NewLine + 
                "https://github.com/Novalys/PokemonGo-Bot-SimpleGUI", "PoGo Bot");
        }
    }
}
