using System.Collections;
using System.Collections.Generic;
using ExileCore;
using ExileCore.PoEMemory.MemoryObjects;
using ExileCore.Shared;
using SharpDX;
using ExileCore.Shared.Enums;
using System.Linq;
using ExileCore.PoEMemory.Elements;
using System;
using System.Security;
using System.Threading.Tasks;
using AutoSextant.PoEStack;

namespace AutoSextant;

public class AutoSextant : BaseSettingsPlugin<AutoSextantSettings>
{
    internal static AutoSextant Instance;

    public override bool Initialise()
    {
        if (Instance == null)
        {
            Instance = this;
        }

        Input.RegisterKey(Settings.RestockHotkey);
        Settings.RestockHotkey.OnValueChanged += () => { Input.RegisterKey(Settings.RestockHotkey); };
        Input.RegisterKey(Settings.CleanInventoryHotKey);
        Settings.CleanInventoryHotKey.OnValueChanged += () => { Input.RegisterKey(Settings.CleanInventoryHotKey); };
        Input.RegisterKey(Settings.RunHotkey);
        Settings.RunHotkey.OnValueChanged += () => { Input.RegisterKey(Settings.RunHotkey); };
        Input.RegisterKey(Settings.CancelHotKey);
        Settings.RunHotkey.OnValueChanged += () => { Input.RegisterKey(Settings.CancelHotKey); };

        Settings.UpdatePoeStackPrices.OnPressed += async () =>
        {
            Log.Debug("Updating PoeStack prices");
            var priceFetcher = new PoEStack.PriceFetcher();
            (Dictionary<string, PoeStackPrice>, Dictionary<string, PoeStackPrice>) result = await priceFetcher.Load(true);
            var Prices = result.Item1;
            var CurrencyPrices = result.Item2;
            // var (Prices, CurrencyPrices) = x.Result;
            CompassList.Prices.Clear();
            foreach (var price in Prices.Values)
            {
                CompassList.Prices.Add(price.Name, new CompassPrice
                {
                    Name = price.Name,
                    ChaosPrice = (int)price.Value,
                    DivinePrice = 0
                });
            }
            CompassList.DivinePrice = (float)CurrencyPrices["Divine Orb"].Value;
            CompassList.AwakenedSextantPrice = (float)CurrencyPrices["Awakened Sextant"].Value;
        };

        var priceFetcher = new PoEStack.PriceFetcher();
        var task = priceFetcher.Load();
        task.ContinueWith((x) =>
        {
            var result = x.Result;
            CompassList.Prices.Clear();
            foreach (var price in result.Item1.Values)
            {
                CompassList.Prices.Add(price.Name, new CompassPrice
                {
                    Name = price.Name,
                    ChaosPrice = (int)price.Value,
                    DivinePrice = 0
                });
            }
            CompassList.DivinePrice = (float)result.Item2["Divine Orb"].Value;
            CompassList.AwakenedSextantPrice = (float)result.Item2["Awakened Sextant"].Value;
        });


        return true;
    }

    public override void AreaChange(AreaInstance area)
    {
        //Perform once-per-zone processing here
        //For example, Radar builds the zone map texture here

        // SellAssistant.SellAssistant.Enable();
        if (SellAssistant.SellAssistant.Enabled)
        {
            SellAssistant.SellAssistant.Disable();
        }
    }

    private string _restockCoroutineName = "AutoSextant_RestockCoroutine";
    private string _dumpCoroutineName = "AutoSextant_DumpCoroutine";
    private string _runCoroutineName = "AutoSextant_RunCoroutine";

    public bool IsAnyRoutineRunning
    {
        get
        {
            return Core.ParallelRunner.FindByName(_restockCoroutineName) != null ||
                   Core.ParallelRunner.FindByName(_dumpCoroutineName) != null ||
                   Core.ParallelRunner.FindByName(_runCoroutineName) != null ||
                   SellAssistant.SellAssistant.IsAnyRoutineRunning ||
                   DumpQueue.Count > 0;
        }
    }

    public void StopAllRoutines(Action callback = null)
    {
        DumpQueue.Clear();
        SellAssistant.SellAssistant.StopAllRoutines();
        var routine = Core.ParallelRunner.FindByName(_restockCoroutineName);
        routine?.Done();
        routine = Core.ParallelRunner.FindByName(_dumpCoroutineName);
        routine?.Done();
        routine = Core.ParallelRunner.FindByName(_runCoroutineName);
        routine?.Done();
        Input.ReleaseCtrl();
        Input.ReleaseShift();
        Input.UnblockMouse();
        Core.ParallelRunner.Run(new Coroutine(Cursor.ReleaseItemOnCursor(callback), this, _dumpCoroutineName));
    }

    public override Job Tick()
    {
        if (DumpQueue.Count > 0 && Core.ParallelRunner.FindByName(_dumpCoroutineName) == null)
        {
            var (mod, amount, callback) = DumpQueue[0];
            DumpQueue.RemoveAt(0);
            Core.ParallelRunner.Run(new Coroutine(Dump(mod, amount, callback), this, _dumpCoroutineName));
        }
        if (Settings.EnableSellAssistant && !SellAssistant.SellAssistant.Enabled)
            SellAssistant.SellAssistant.Enable();
        else if (!Settings.EnableSellAssistant && SellAssistant.SellAssistant.Enabled)
            SellAssistant.SellAssistant.Disable();


        if (Settings.CancelHotKey.PressedOnce())
        {
            StopAllRoutines();
            return null;
        }
        if (Settings.RestockHotkey.PressedOnce())
        {
            Settings.EnableSellAssistant.Value = true;
            // Core.ParallelRunner.Run(new Coroutine(Test(), this, _restockCoroutineName));
            // if (Core.ParallelRunner.FindByName(_restockCoroutineName) == null)
            // {
            //     Core.ParallelRunner.Run(new Coroutine(Restock(), this, _restockCoroutineName));
            // }
            // else
            // {
            //     StopAllRoutines();
            // }
        }
        if (Settings.CleanInventoryHotKey.PressedOnce())
        {
            TriggerCleanInventory();
        }
        if (Settings.RunHotkey.PressedOnce())
        {
            if (Core.ParallelRunner.FindByName(_runCoroutineName) == null)
            {
                Core.ParallelRunner.Run(new Coroutine(RunFromInventory(), this, _runCoroutineName));
            }
            else
            {
                StopAllRoutines();
            }
        }

        SellAssistant.SellAssistant.Tick();
        return null;
    }

    private IEnumerator Test()
    {
        if (NStash.Stash.ActiveTab.Name == "Currency")
        {
            yield return NStash.Stash.SelectTab("CHARGED1");
        }
        else
        {
            yield return NStash.Stash.SelectTab("Currency");
        }
    }

    public void TriggerCleanInventory()
    {
        StopAllRoutines(() =>
        {
            Core.ParallelRunner.Run(new Coroutine(CleanInventory(), this, "AutoSextant_CleanInventory"));
        });
    }

    public IEnumerator CleanInventory()
    {
        yield return EnsureStash();
        yield return Cursor.ReleaseItemOnCursor();
        yield return Dump();
        yield return NStash.Stash.SelectTab(Settings.RestockSextantFrom);
        var stashableCurrency = NInventory.Inventory.GetByName(Item.ItemNames[ItemType.Sextant], Item.ItemNames[ItemType.Compass]);
        Input.HoldCtrl();
        foreach (var item in stashableCurrency)
        {
            yield return Input.ClickElement(item.GetClientRect().Center);
            yield return new WaitTime(10);
        }
        Input.ReleaseCtrl();
        Stock.RunRefresh();
    }

    public IEnumerator OpenAtlasInInventoryMode()
    {
        if (GameController.IngameState.IngameUi.Atlas.IsVisible && GameController.IngameState.IngameUi.InventoryPanel.IsVisible && GameController.IngameState.IngameUi.StashElement.IsVisible)
        {
            yield break;
        }
        yield return EnsureEverythingIsClosed();

        var itemsOnGround = GameController.IngameState.IngameUi.ItemsOnGroundLabels;

        foreach (LabelOnGround labelOnGround in itemsOnGround)
        {
            if (!labelOnGround.ItemOnGround.Path.Contains("/Stash"))
            {
                continue;
            }
            if (!labelOnGround.IsVisible)
            {
                Log.Error("Stash not visible");
                yield break;
            }
            // click 150px to the right
            yield return Input.ClickElement(labelOnGround.Label.GetClientRect().Center + new Vector2(150, 0));
        }

        yield return new WaitTime(800);


        itemsOnGround = GameController.IngameState.IngameUi.ItemsOnGroundLabels;

        foreach (LabelOnGround labelOnGround in itemsOnGround)
        {
            if (!labelOnGround.ItemOnGround.Path.Contains("/Stash"))
            {
                continue;
            }
            if (!labelOnGround.IsVisible)
            {
                Log.Error("Stash not visible");
                yield break;
            }
            // click 150px to the right
            yield return Input.ClickElement(labelOnGround.Label.GetClientRect().Center);
        }

        Input.KeyDown(Settings.AtlasHotKey.Value);
        Input.KeyUp(Settings.AtlasHotKey.Value);

        yield return new WaitFunctionTimed(() => GameController.IngameState.IngameUi.Atlas.IsVisible, true, 1000, "Atlas not opened");

        Input.KeyDown(Settings.InventoryHotKey.Value);
        Input.KeyUp(Settings.InventoryHotKey.Value);

        yield return new WaitFunctionTimed(() => GameController.IngameState.IngameUi.InventoryPanel != null && GameController.IngameState.IngameUi.InventoryPanel.IsVisible && GameController.IngameState.IngameUi.StashElement.IsVisible, true, 1000, "Inventory not opened");
    }

    public IEnumerator RunFromInventory()
    {
        yield return Stock.Refresh();
        yield return OpenAtlasInInventoryMode();

        if (!Atlas.HasBlockMods)
        {
            LogError("No block mods");
            yield break;
        }

        var stone = new VoidStone(VoidStonePosition.Top);
        var maxCharged = 12 * 5;

        var capacity = Stock.Capacity;
        var totalStock = Stock.Count;
        if (totalStock > capacity)
        {
            TriggerCleanInventory();
            yield break;
        }
        var maxCompassesThisSession = Math.Min(maxCharged, capacity - totalStock);
       
        var holdingShift = false;
        // TODO: attempt counter, then if too many, clear cursor and try again
        while (Inventory.TotalChargedCompasses < maxCompassesThisSession)
        {
            yield return NStash.Stash.SelectTab(Settings.RestockSextantFrom);
            var compassPrice = stone.Price;
            var currentName = compassPrice?.Name ?? null;
            
            bool capOk = true;
            int index, cap;
            string _;
            bool never = false, 
                always;
            if (currentName != null)
            {
                index = Settings.ModSettings.FindIndex(x => x.Item1 == currentName);
                (_, never, always, cap) = index != -1 ? Settings.ModSettings[index] : (currentName, false, false, 0);
                capOk = cap > 0 ? Stock.Get(CompassList.PriceToModName[currentName]) < cap : true;
            }

            if (never || compassPrice == null || compassPrice.ChaosPrice < Settings.MinChaosValue || !capOk)
            {
                var nextSextant = Stash.NextSextant;
                if (nextSextant == null)
                {
                    TriggerCleanInventory();
                    break;
                }
                if (!holdingShift)
                {
                    yield return Input.UseItem(nextSextant.Position);
                    holdingShift = true;
                    Input.KeyDown(System.Windows.Forms.Keys.ShiftKey);
                }
                yield return Input.ClickElement(stone.Position);
                SessionWindow.IncreaseSextantCount();
                yield return new WaitFunctionTimed(() => stone.Price != null && stone.Price.Name != currentName, false, 500);
                if (stone.Price == null || stone.Price.Name == currentName)
                {
                    holdingShift = false;
                    Input.KeyUp(System.Windows.Forms.Keys.ShiftKey);
                    yield return Cursor.ReleaseItemOnCursor();
                    // Didn't work or was the same, try again
                    continue;
                }
            }

            compassPrice = stone.Price;

            index = Settings.ModSettings.FindIndex(x => x.Item1 == compassPrice.Name);
            (_, never, always, cap) = index != -1 ? Settings.ModSettings[index] : (compassPrice.Name, false, false, 0);

            capOk = cap > 0 ? Stock.Get(CompassList.PriceToModName[compassPrice.Name]) < cap : true;
            if ((!never && stone.Price != null && compassPrice.ChaosPrice >= Settings.MinChaosValue && capOk) || (always && capOk))
            {
                holdingShift = false;
                Input.KeyUp(System.Windows.Forms.Keys.ShiftKey);
                var nextCompass = Stash.NextCompass;
                var nextFreeSlot = Inventory.NextFreeChargedCompassSlot;
                if (nextCompass == null || nextFreeSlot == null)
                {
                    break;
                }
                while (Cursor.ItemName == null || Cursor.ItemName != "Charged Compass")
                {
                    yield return Input.UseItem(nextCompass.Position);
                    yield return Input.ClickElement(stone.Position);
                    yield return new WaitFunctionTimed(() => Cursor.ItemName != null && Cursor.ItemName == "Charged Compass", false, 500);
                }
                SessionWindow.AddMod(compassPrice.Name);
                var count = NInventory.Inventory.InventoryCount;
                while (NInventory.Inventory.InventoryCount == count && (Cursor.ItemName != null && Cursor.ItemName == "Charged Compass"))
                {
                    yield return Input.ClickElement(nextFreeSlot.Position);
                    yield return new WaitFunctionTimed(() => NInventory.Inventory.InventoryCount != count && (Cursor.ItemName == null), false, 500);
                }
            }
        }
        holdingShift = false;
        Input.KeyUp(System.Windows.Forms.Keys.ShiftKey);

        yield return Dump();

        yield return RunFromInventory();
    }

    public IEnumerator Run()
    {
        yield return Stock.Refresh();
        yield return EnsureAtlas();

        if (!Atlas.HasBlockMods)
        {
            LogError("No block mods");
            yield break;
        }

        var stone = new VoidStone(VoidStonePosition.Top);
        var maxCharged = 7 * 5;

        var holdingShift = false;
        while (Inventory.TotalChargedCompasses < maxCharged)
        {
            var compassPrice = stone.Price;
            var currentName = compassPrice?.Name ?? null;

            bool capOk = true;
            int index, cap;
            string _;
            bool never, always;
            if (currentName != null)
            {
                index = Settings.ModSettings.FindIndex(x => x.Item1 == currentName);
                (_, never, always, cap) = index != -1 ? Settings.ModSettings[index] : (currentName, false, false, 0);
                capOk = cap > 0 ? Stock.Get(CompassList.PriceToModName[currentName]) < cap : true;
            }

            if (compassPrice == null || compassPrice.ChaosPrice < Settings.MinChaosValue || !capOk)
            {
                var nextSextant = Inventory.NextSextant;
                if (nextSextant == null)
                {
                    break;
                }
                if (!holdingShift)
                {
                    holdingShift = true;
                    Input.KeyDown(System.Windows.Forms.Keys.ShiftKey);
                    yield return Input.ClickElement(nextSextant.Position, System.Windows.Forms.MouseButtons.Right);
                }
                yield return Input.ClickElement(stone.Position);
                SessionWindow.IncreaseSextantCount();
                yield return new WaitFunctionTimed(() => stone.Price != null && stone.Price.Name != currentName, false, 500);
                if (stone.Price == null || stone.Price.Name == currentName)
                {
                    yield return new WaitTime(50);
                    // Didn't work or was the same, try again
                    continue;
                }
            }

            compassPrice = stone.Price;

            index = Settings.ModSettings.FindIndex(x => x.Item1 == compassPrice.Name);
            (_, never, always, cap) = index != -1 ? Settings.ModSettings[index] : (compassPrice.Name, false, false, 0);

            capOk = cap > 0 ? Stock.Get(CompassList.PriceToModName[compassPrice.Name]) < cap : true;
            if ((!never && stone.Price != null && compassPrice.ChaosPrice >= Settings.MinChaosValue && capOk) || (always && capOk))
            {
                holdingShift = false;
                Input.KeyUp(System.Windows.Forms.Keys.ShiftKey);
                var nextCompass = Inventory.NextCompass;
                var nextFreeSlot = Inventory.NextFreeChargedCompassSlot;
                if (nextCompass == null || nextFreeSlot == null)
                {
                    break;
                }
                yield return Input.ClickElement(nextCompass.Position, System.Windows.Forms.MouseButtons.Right);
                yield return Input.ClickElement(stone.Position);
                SessionWindow.AddMod(compassPrice.Name);
                yield return Input.ClickElement(nextFreeSlot.Position);
            }
        }
        holdingShift = false;
        Input.KeyUp(System.Windows.Forms.Keys.ShiftKey);

        yield return Dump();
        yield return Restock();

        yield return Run();
    }

    public IEnumerator Restock()
    {
        yield return EnsureStash();

        var tab1 = Stash.GetStashTabIndexForName(Settings.RestockSextantFrom.Value);
        yield return NStash.Stash.SelectTab(tab1);

        yield return RestockColumn(0, ItemType.Compass, true);
        yield return RestockColumn(1, ItemType.Sextant, true);
        yield return RestockColumn(2, ItemType.Sextant);
        yield return RestockColumn(3, ItemType.Sextant);
        yield return RestockColumn(4, ItemType.Sextant);
    }

    public IEnumerator EnsureAtlas()
    {
        if (GameController.IngameState.IngameUi.Atlas.IsVisible && !GameController.IngameState.IngameUi.InventoryPanel.IsVisible)
        {
            yield break;
        }
        yield return EnsureEverythingIsClosed();

        Input.KeyDown(Settings.AtlasHotKey.Value);
        Input.KeyUp(Settings.AtlasHotKey.Value);

        yield return new WaitFunctionTimed(() => GameController.IngameState.IngameUi.Atlas.IsVisible, true, 1000, "Atlas not opened");

        Input.KeyDown(Settings.InventoryHotKey.Value);
        Input.KeyUp(Settings.InventoryHotKey.Value);

        yield return new WaitFunctionTimed(() => GameController.IngameState.IngameUi.InventoryPanel != null && GameController.IngameState.IngameUi.InventoryPanel.IsVisible, true, 1000, "Inventory not opened");
    }

    private static List<(string, int, Action<int>)> DumpQueue = new List<(string, int, Action<int>)>();

    public void TriggerDump(string specificMod = null, int? specifiedAmount = null, Action<int> callback = null)
    {
        DumpQueue.Add((specificMod, specifiedAmount ?? 60, callback));
    }

    public IEnumerator Dump(string specificMod = null, int? specifiedAmount = null, Action<int> callback = null)
    {
        yield return EnsureStash();

        var dumpTabs = Settings.DumpTabs.Value.Split(',');

        long leftToDump = specifiedAmount ?? 60;
        long dumped = 0;
        while ((specificMod == null ? Inventory.TotalChargedCompasses : NInventory.Inventory.GetByModName(specificMod).Count()) > 0)
        {
            foreach (var tabName in dumpTabs)
            {
                if (Stock.Tabs.ContainsKey(tabName) && Stock.Tabs[tabName].Values.Sum() >= 576)
                    continue;

                var items = specificMod == null ? Inventory.ChargedCompasses : NInventory.Inventory.GetByModName(specificMod).Select(x => new Item(x)).ToArray();
                var tab = Stash.GetStashTabIndexForName(tabName);
                yield return NStash.Stash.SelectTab(tab);
                yield return new WaitTime(30);
                var stashTabType = GameController.IngameState.IngameUi.StashElement.VisibleStash.InvType;
                var max = stashTabType == InventoryType.QuadStash ? 576 : 144;
                var count = GameController.IngameState.IngameUi.StashElement.VisibleStash.ItemCount;
                if (count >= max)
                {
                    continue;
                }
                var freeSlots = max - count;
                var toDump = Math.Min(Math.Min(freeSlots, items.Count()), leftToDump);
                Input.KeyDown(System.Windows.Forms.Keys.ControlKey);
                for (int i = 0; i < toDump; i++)
                {
                    yield return Input.ClickElement(items[i].Position, 10);
                }
                leftToDump -= toDump;
                dumped += toDump;
                Input.KeyUp(System.Windows.Forms.Keys.ControlKey);
                yield return new WaitTime(30);
                if (toDump == items.Count() || leftToDump <= 0)
                {
                    callback?.Invoke((int)dumped);
                    yield break;
                }
            }
        }
        callback?.Invoke((int)dumped);
    }

    public IEnumerator EnsureStash()
    {
        if (GameController.IngameState.IngameUi.StashElement.IsVisible && GameController.IngameState.IngameUi.InventoryPanel.IsVisible)
        {
            yield break;
        }
        yield return EnsureEverythingIsClosed();

        var itemsOnGround = GameController.IngameState.IngameUi.ItemsOnGroundLabels;
        var stash = GameController.IngameState.IngameUi.StashElement;

        if (stash is { IsVisible: true })
        {
            yield break;
        }

        foreach (LabelOnGround labelOnGround in itemsOnGround)
        {
            if (!labelOnGround.ItemOnGround.Path.Contains("/Stash"))
            {
                continue;
            }
            if (!labelOnGround.IsVisible)
            {
                Log.Error("Stash not visible");
                yield break;
            }
            yield return Input.ClickElement(labelOnGround.Label.GetClientRect().Center);
            yield return new WaitFunctionTimed(() => stash is { IsVisible: true }, true, 2000, "Stash not reached in time");
            if (stash is { IsVisible: false })
            {
                Log.Error("Stash not visible");
                yield break;
            }
        }
        yield return true;
    }

    private IEnumerator EnsureEverythingIsClosed()
    {
        if (GameController.IngameState.IngameUi.Atlas.IsVisible)
        {
            Input.KeyDown(Settings.AtlasHotKey.Value);
            Input.KeyUp(Settings.AtlasHotKey.Value);
            yield return new WaitFunctionTimed(() => !GameController.IngameState.IngameUi.Atlas.IsVisible, true, 1000, "Atlas not closed");
        }
        if (GameController.IngameState.IngameUi.InventoryPanel != null && GameController.IngameState.IngameUi.InventoryPanel.IsVisible)
        {
            Input.KeyDown(Settings.InventoryHotKey.Value);
            Input.KeyUp(Settings.InventoryHotKey.Value);
            yield return new WaitFunctionTimed(() => !GameController.IngameState.IngameUi.InventoryPanel.IsVisible, true, 1000, "Inventory not closed");
        }
        if (GameController.IngameState.IngameUi.StashElement != null && GameController.IngameState.IngameUi.StashElement.IsVisible)
        {
            Input.KeyDown(System.Windows.Forms.Keys.Escape);
            Input.KeyUp(System.Windows.Forms.Keys.Escape);
            yield return new WaitFunctionTimed(() => !GameController.IngameState.IngameUi.StashElement.IsVisible, true, 1000, "Inventory not closed");
        }
    }

    private IEnumerator RestockColumn(int col, ItemType type, bool firstColumn = false)
    {
        var compassItem = Stash.GetFirstItemTypeFromStash(Item.ItemNames[type]);
        if (compassItem == null)
        {
            // Only stop the entire process on entry of this function as we can still work with a partial restock
            if (firstColumn)
                TriggerCleanInventory();
            yield break;
        }
        var itemWidth = compassItem.GetClientRect().Width / 56 * 70;
        var inventoryPanel = Instance.GameController.Game.IngameState.IngameUi.InventoryPanel[InventoryIndex.PlayerInventory];
        Vector2 inventoryPanelPosition = inventoryPanel.InventoryUIElement.GetClientRect().TopLeft;

        while (Inventory.CountItemsInColumn(col) + 10 <= 50)
        {
            var item = Stash.GetFirstItemTypeFromStash(Item.ItemNames[type]);
            if (compassItem == null)
                yield break;
            var itemStack = new Item(item);
            yield return itemStack.GetStack(true);
            yield return new WaitTime(1);
        }


        var column = Inventory.GetColumn(col);
        for (int i = 0; i < column.Count(); i++)
        {
            var slot = column[i];
            var item = Stash.GetFirstItemTypeFromStash(Item.ItemNames[type]);
            if (compassItem == null)
                yield break;
            var compassesStack = item.Item.GetComponent<ExileCore.PoEMemory.Components.Stack>();
            var compassStackSize = compassesStack?.Size ?? 0;

            if (compassStackSize <= 0)
            {
                break;
            }

            Vector2 position;
            int sizeNeeded;
            if (slot == null)
            {
                sizeNeeded = 10;
                position = new Vector2(
                    inventoryPanelPosition.X + itemWidth / 2 + itemWidth * col,
                    inventoryPanelPosition.Y + itemWidth / 2 + itemWidth * i
                );
            }
            else
            {
                var slotStack = slot.Item.GetComponent<ExileCore.PoEMemory.Components.Stack>();
                var slotStackSize = slotStack?.Size ?? 0;
                sizeNeeded = 10 - slotStackSize;
                position = slot.GetClientRect().Center;
            }

            if (compassStackSize > sizeNeeded && sizeNeeded > 0)
            {
                var itemStack = new Item(item);
                if (sizeNeeded < 10)
                {
                    yield return itemStack.GetFraction(sizeNeeded);
                }
                else
                {
                    yield return itemStack.GetStack();
                }
                yield return Input.ClickElement(position);
            }
        }
    }

    public override void Render()
    {
        if (IsAnyRoutineRunning)
        {
            Graphics.DrawText("AutoSextant is busy", new System.Numerics.Vector2(100, 100), Color.Red, 20);
        }
        SessionWindow.Render();
        Error.Render();
        SellAssistant.SellAssistant.Render();
        if (Settings.PositionDebug.Value && GameController.IngameState.IngameUi.InventoryPanel.IsVisible)
        {
            var free = Inventory.NextFreeChargedCompassSlot;
            if (free != null)
            {
                var newRect = new RectangleF(free.Position.X - 35, free.Position.Y - 35, 70, 70);
                Graphics.DrawFrame(newRect, Color.Green, 100f, 3, 0);
            }
        }
        var InnerAtlas = GameController.IngameState.IngameUi.Atlas.InnerAtlas;
        if (InnerAtlas.IsVisible)
        {
            VoidStone[] blockStones = {
                new(VoidStonePosition.Left),
                new(VoidStonePosition.Right),
                new(VoidStonePosition.Bottom)
            };

            foreach (var blockStone in blockStones)
            {
                var modName = blockStone.ModName;

                var isBlocked = modName != null &&
                                (Settings.UseModsForBlockingGroup1.Value.Contains(modName) ||
                                Settings.UseModsForBlockingGroup2.Value.Contains(modName) ||
                                Settings.UseModsForBlockingGroup3.Value.Contains(modName));

                var rect = blockStone.Slot.GetClientRect();
                // make new rect that is half the width and half the height and adjust the x and y to be in the center
                var newRect = new RectangleF(rect.X + rect.Width / 4, rect.Y + rect.Height / 4, rect.Width / 2,
                    rect.Height / 2);
                if (isBlocked)
                {
                    Graphics.DrawFrame(newRect, Color.Green, 100f, 3, 0);
                }
                else
                {
                    Graphics.DrawFrame(newRect, Color.Red, 100f, 3, 0);
                }
            }

            var rollingStone = new VoidStone(VoidStonePosition.Top);
            var compassPrice = rollingStone.Price;

            if (compassPrice != null)
            {
                string currentName = compassPrice.Name;
                var index = Settings.ModSettings.FindIndex(x => x.Item1 == currentName);
                var (_, never, always, cap) = index != -1 ? Settings.ModSettings[index] : (currentName, false, false, 0);
                var capOk = cap > 0 ? Stock.Get(CompassList.PriceToModName[currentName]) < cap : true;

                var chaosPrice = compassPrice.ChaosPrice;
                var color = !never && capOk && (chaosPrice >= Settings.MinChaosValue || (always && capOk)) ? Color.Green : Color.Red;
                Graphics.DrawFrame(rollingStone.Slot.GetClientRect(), color, 100f, 3, 0);
                var txt = $"{rollingStone.ClearName} - {chaosPrice} Chaos";
                if (never)
                    txt += " (Never)";
                else if (always && capOk)
                    txt += " (Always)";
                else if (!capOk)
                    txt += $" (Cap reached: {cap})";

                var textSize = Graphics.MeasureText(txt, 20);
                var textPos = new System.Numerics.Vector2
                {
                    X = rollingStone.Slot.GetClientRect().Center.X - textSize.X / 2,
                    Y = rollingStone.Slot.GetClientRect().Top - 20
                };
                Graphics.DrawText(txt, textPos, color, 21);
            }
            else
            {
                Graphics.DrawFrame(rollingStone.Slot.GetClientRect(), Color.Red, 100f, 3, 0);
            }

        }
    }

    public override void EntityAdded(Entity entity)
    {
        //If you have a reason to process every entity only once,
        //this is a good place to do so.
        //You may want to use a queue and run the actual
        //processing (if any) inside the Tick method.
    }
}