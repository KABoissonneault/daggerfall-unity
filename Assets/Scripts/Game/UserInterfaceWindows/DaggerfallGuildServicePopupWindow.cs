// Project:         Daggerfall Tools For Unity
// Copyright:       Copyright (C) 2009-2018 Daggerfall Workshop
// Web Site:        http://www.dfworkshop.net
// License:         MIT License (http://www.opensource.org/licenses/mit-license.php)
// Source Code:     https://github.com/Interkarma/daggerfall-unity
// Original Author: Hazelnut
// Contributors:    
// 
// Notes:
//

using UnityEngine;
using DaggerfallWorkshop.Utility;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.Items;
using DaggerfallWorkshop.Game.UserInterface;
using DaggerfallConnect;
using DaggerfallConnect.Arena2;
using System.Collections.Generic;
using DaggerfallWorkshop.Game.Questing;
using System;
using DaggerfallWorkshop.Game.Guilds;
using DaggerfallWorkshop.Game.Formulas;
using DaggerfallConnect.FallExe;

namespace DaggerfallWorkshop.Game.UserInterfaceWindows
{
    public class DaggerfallGuildServicePopupWindow : DaggerfallQuestPopupWindow, IMacroContextProvider
    {
        #region UI Rects

        Rect joinButtonRect = new Rect(5, 5, 120, 7);
        Rect talkButtonRect = new Rect(5, 14, 120, 7);
        Rect serviceButtonRect = new Rect(5, 23, 120, 7);
        Rect exitButtonRect = new Rect(44, 33, 43, 15);

        #endregion

        #region UI Controls

        Panel mainPanel = new Panel();
        Button joinButton = new Button();
        Button talkButton = new Button();
        Button serviceButton = new Button();
        Button exitButton = new Button();
        TextLabel serviceLabel = new TextLabel();

        #endregion

        #region Fields

        const string baseTextureName = "GILD00I0.IMG";      // Join Guild / Talk / Service
        const string memberTextureName = "GILD01I0.IMG";      // Join Guild / Talk / Service

        const int TrainingOfferId = 8;
        const int TrainingTooSkilledId = 4022;
        const int TrainingToSoonId = 4023;
        const int TrainSkillId = 5221;
        const int InsufficientRankId = 3100;
        const int TooGenerousId = 702;
        const int DonationThanksId = 703;
        const int SorcerorMagickaRecharge = 465;

        Texture2D baseTexture;
        PlayerEntity playerEntity;
        GuildManager guildManager;

        FactionFile.GuildGroups guildGroup;
        StaticNPC serviceNPC;
        GuildNpcServices npcService;
        GuildServices service;
        int buildingFactionId;  // Needed for temples & orders

        Guild guild;
        PlayerGPS.DiscoveredBuilding buildingDiscoveryData;
        int curingCost = 0;

        static ItemCollection merchantItems;    // Temporary

        #endregion

        #region Constructors

        public DaggerfallGuildServicePopupWindow(IUserInterfaceManager uiManager, StaticNPC npc, FactionFile.GuildGroups guildGroup, int buildingFactionId)
            : base(uiManager)
        {
            playerEntity = GameManager.Instance.PlayerEntity;
            guildManager = GameManager.Instance.GuildManager;

            serviceNPC = npc;
            npcService = (GuildNpcServices) npc.Data.factionID;
            service = Services.GetService(npcService);
            Debug.Log("NPC offers guild service: " + service.ToString());

            this.guildGroup = guildGroup;
            this.buildingFactionId = buildingFactionId;

            guild = guildManager.GetGuild(guildGroup, buildingFactionId);

            // Clear background
            ParentPanel.BackgroundColor = Color.clear;
        }

        #endregion

        #region Setup Methods

        protected override void Setup()
        {
            // Ascertain guild membership status, exempt Thieves Guild and Dark Brotherhood since should never find em until a member
            bool member = guildManager.GetGuild(guildGroup).IsMember();
            if (guildGroup == FactionFile.GuildGroups.DarkBrotherHood || guildGroup == FactionFile.GuildGroups.GeneralPopulace)
                member = true;

            // Load all textures
            LoadTextures(member);

            // Create interface panel
            mainPanel.HorizontalAlignment = HorizontalAlignment.Center;
            mainPanel.VerticalAlignment = VerticalAlignment.Middle;
            mainPanel.BackgroundTexture = baseTexture;
            mainPanel.Position = new Vector2(0, 50);
            mainPanel.Size = new Vector2(130, 51);

            // Join Guild button
            if (!member)
            {
                joinButton = DaggerfallUI.AddButton(joinButtonRect, mainPanel);
                joinButton.OnMouseClick += JoinButton_OnMouseClick;
            }

            // Talk button
            talkButton = DaggerfallUI.AddButton(talkButtonRect, mainPanel);
            talkButton.OnMouseClick += TalkButton_OnMouseClick;

            // Service button
            serviceLabel.Position = new Vector2(0, 1);
            serviceLabel.ShadowPosition = Vector2.zero;
            serviceLabel.HorizontalAlignment = HorizontalAlignment.Center;
            serviceLabel.Text = Services.GetServiceLabelText(service);
            serviceButton = DaggerfallUI.AddButton(serviceButtonRect, mainPanel);
            serviceButton.Components.Add(serviceLabel);
            serviceButton.OnMouseClick += ServiceButton_OnMouseClick;

            // Exit button
            exitButton = DaggerfallUI.AddButton(exitButtonRect, mainPanel);
            exitButton.OnMouseClick += ExitButton_OnMouseClick;

            NativePanel.Components.Add(mainPanel);
        }

        public override void OnPush()
        {
            base.OnPush();

            buildingDiscoveryData = GameManager.Instance.PlayerEnterExit.BuildingDiscoveryData;
            
            // Check guild advancement
            TextFile.Token[] updatedRank = guild.UpdateRank(playerEntity);
            if (updatedRank != null)
            {
                DaggerfallMessageBox messageBox = new DaggerfallMessageBox(uiManager, uiManager.TopWindow);
                messageBox.SetTextTokens(updatedRank, guild);
                messageBox.ClickAnywhereToClose = true;
                messageBox.Show();
            }
            // Check for free healing (Temple members)
            if (guild.FreeHealing() && playerEntity.CurrentHealth < playerEntity.MaxHealth)
            {
                playerEntity.SetHealth(playerEntity.MaxHealth);
                DaggerfallUI.MessageBox(350);
            }
            // Check for magicka restoration (sorcerers)
            if (guild.FreeMagickaRecharge() && playerEntity.CurrentMagicka < playerEntity.MaxMagicka)
            {
                DaggerfallMessageBox msgBox = new DaggerfallMessageBox(uiManager, this);
                msgBox.SetTextTokens(DaggerfallUnity.Instance.TextProvider.GetRandomTokens(SorcerorMagickaRecharge));
                msgBox.ClickAnywhereToClose = true;
                msgBox.Show();

                // Refill magicka
                playerEntity.SetMagicka(playerEntity.MaxMagicka);
            }
        }

        #endregion

        #region Private Methods

        // TODO: Classic seems deterministic so when re-visiting each mages guildhall, player sees same stuff.
        // Does it change with level? Is it always generated but uses a consistent seed? How should DFU do this?
        ItemCollection GetMerchantMagicItems()
        {
            PlayerEntity playerEntity = GameManager.Instance.PlayerEntity;
            ItemCollection items = new ItemCollection();
            int numOfItems = (buildingDiscoveryData.quality / 2) + 1;

            for (int i = 0; i <= numOfItems; i++)
            {
                // Create magic item which is already identified
                DaggerfallUnityItem magicItem = ItemBuilder.CreateRandomMagicItem(playerEntity.Level, playerEntity.Gender, playerEntity.Race);
                magicItem.IdentifyItem();
                items.AddItem(magicItem);
            }

            items.AddItem(ItemBuilder.CreateItem(ItemGroups.MiscItems, (int)MiscItems.Spellbook));

            if (guild.CanAccessService(GuildServices.BuySoulgems))
            {
                for (int i = 0; i <= numOfItems; i++)
                {
                    DaggerfallUnityItem magicItem = ItemBuilder.CreateItem(ItemGroups.MiscItems, (int)MiscItems.Soul_trap);
                    magicItem.value = 5000;

                    if (UnityEngine.Random.Range(1, 101) >= 25)
                        magicItem.TrappedSoulType = MobileTypes.None;
                    else
                    {
                        int id = UnityEngine.Random.Range(0, 43);
                        magicItem.TrappedSoulType = (MobileTypes)id;
                        MobileEnemy mobileEnemy = GameObjectHelper.EnemyDict[id];
                        magicItem.value += mobileEnemy.SoulPts;
                    }
                    items.AddItem(magicItem);
                }
            }
            return items;
        }

        // TODO: classic seems to generate each time player select buy potions.. should we make more persistent for DFU?
        ItemCollection GetMerchantPotions()
        {
            ItemCollection potions = new ItemCollection();
            for (int n = UnityEngine.Random.Range(12, 20); n > 0; n--)
                potions.AddItem(ItemBuilder.CreateRandomPotion());
            return potions;
        }

        void LoadTextures(bool member)
        {
            baseTexture = ImageReader.GetTexture(member ? memberTextureName : baseTextureName);
        }

        #endregion

        #region Event Handlers: General

        private void TalkButton_OnMouseClick(BaseScreenComponent sender, Vector2 position)
        {
            GameManager.Instance.TalkManager.TalkToStaticNPC(serviceNPC);
        }

        private void ServiceButton_OnMouseClick(BaseScreenComponent sender, Vector2 position)
        {
            // Check access to service
            if (!guild.CanAccessService(service))
            {
                if (guild.IsMember())
                {
                    DaggerfallMessageBox msgBox = new DaggerfallMessageBox(uiManager, this);
                    msgBox.SetTextTokens(DaggerfallUnity.Instance.TextProvider.GetRandomTokens(InsufficientRankId));
                    msgBox.ClickAnywhereToClose = true;
                    msgBox.Show();
                }
                else
                {
                    DaggerfallUI.MessageBox(HardStrings.serviceMembersOnly);
                }
                return;
            }
            // Handle known service
            switch (service)
            {
                case GuildServices.Quests:
                    GetQuest();
                    break;

                case GuildServices.Identify:
                    CloseWindow();
                    uiManager.PushWindow(new DaggerfallTradeWindow(uiManager, DaggerfallTradeWindow.WindowModes.Identify, this, guild));
                    break;

                case GuildServices.Repair:
                    CloseWindow();
                    uiManager.PushWindow(new DaggerfallTradeWindow(uiManager, DaggerfallTradeWindow.WindowModes.Repair, this, guild));
                    break;

                case GuildServices.Training:
                    TrainingService();
                    break;

                case GuildServices.Donate:
                    DonationService();
                    break;

                case GuildServices.CureDisease:
                    CureDiseaseService();
                    break;

                case GuildServices.BuyPotions:
                    CloseWindow();
                    uiManager.PushWindow(new DaggerfallTradeWindow(uiManager, DaggerfallTradeWindow.WindowModes.Buy, this, guild) {
                        MerchantItems = GetMerchantPotions()
                    });
                    break;

                case GuildServices.MakePotions:
                    MakePotionService();
                    break;

                case GuildServices.BuySpells:
                    CloseWindow();
                    uiManager.PushWindow(new DaggerfallSpellBookWindow(uiManager, this, true));
                    break;

                case GuildServices.MakeSpells:
                    CloseWindow();
                    uiManager.PushWindow(DaggerfallUI.Instance.DfSpellMakerWindow);
                    break;

                case GuildServices.BuyMagicItems:   // TODO: switch items depending on npcService?
                    CloseWindow();
                    uiManager.PushWindow(new DaggerfallTradeWindow(uiManager, DaggerfallTradeWindow.WindowModes.Buy, this, guild) {
                        MerchantItems = GetMerchantMagicItems()
                    });
                    break;

                case GuildServices.MakeMagicItems:
                    CloseWindow();
                    uiManager.PushWindow(DaggerfallUI.Instance.DfItemMakerWindow);
                    break;

                case GuildServices.SellMagicItems:
                    CloseWindow();
                    uiManager.PushWindow(new DaggerfallTradeWindow(uiManager, DaggerfallTradeWindow.WindowModes.SellMagic, this, guild));
                    break;

                case GuildServices.Teleport:
                    CloseWindow();
                    DaggerfallUI.Instance.DfTravelMapWindow.ActivateTeleportationTravel();
                    uiManager.PushWindow(DaggerfallUI.Instance.DfTravelMapWindow);
                    break;

                case GuildServices.DaedraSummoning:
                    DaedraSummoningService((int) npcService);
                    break;

                case GuildServices.ReceiveArmor:
                    ReceiveArmorService();
                    break;

                case GuildServices.ReceiveHouse:
                    ReceiveHouseService();
                    break;

                case GuildServices.Spymaster:
                    const int spyMasterGreetingTextId = 402;
                    DaggerfallMessageBox msgBox = new DaggerfallMessageBox(uiManager, this);
                    msgBox.SetTextTokens(DaggerfallUnity.Instance.TextProvider.GetRandomTokens(spyMasterGreetingTextId));
                    msgBox.ClickAnywhereToClose = true;
                    msgBox.OnClose += SpyMasterGreetingPopUp_OnClose;
                    msgBox.Show();                    
                    break;

                default:
                    CloseWindow();
                    Services.CustomGuildService customService;
                    if (Services.GetCustomGuildService((int)service, out customService))
                        customService();
                    else
                        DaggerfallUI.MessageBox("Guild service not yet implemented.");
                    break;
            }
        }

        private void ExitButton_OnMouseClick(BaseScreenComponent sender, Vector2 position)
        {
            CloseWindow();
        }

        #endregion

        #region Event Handlers: Joining Guild

        private void JoinButton_OnMouseClick(BaseScreenComponent sender, Vector2 position)
        {
            CloseWindow();
            guild = guildManager.JoinGuild(guildGroup, buildingFactionId);
            if (guild == null)
            {
                DaggerfallUI.MessageBox("Joining guild " + guildGroup + " not implemented.");
            }
            else if (!guild.IsMember())
            {
                DaggerfallMessageBox messageBox = new DaggerfallMessageBox(uiManager, uiManager.TopWindow);
                if (guild.IsEligibleToJoin(playerEntity))
                {
                    messageBox.SetTextTokens(guild.TokensEligible(playerEntity), guild);
                    messageBox.AddButton(DaggerfallMessageBox.MessageBoxButtons.Yes);
                    messageBox.AddButton(DaggerfallMessageBox.MessageBoxButtons.No);
                    messageBox.OnButtonClick += ConfirmJoinGuild_OnButtonClick;
                }
                else
                {
                    messageBox.SetTextTokens(guild.TokensIneligible(playerEntity), guild);
                    messageBox.ClickAnywhereToClose = true;
                }
                messageBox.Show();
            }
        }

        public void ConfirmJoinGuild_OnButtonClick(DaggerfallMessageBox sender, DaggerfallMessageBox.MessageBoxButtons messageBoxButton)
        {
            sender.CloseWindow();
            if (messageBoxButton == DaggerfallMessageBox.MessageBoxButtons.Yes)
            {
                guildManager.AddMembership(guildGroup, guild);

                DaggerfallMessageBox messageBox = new DaggerfallMessageBox(uiManager, uiManager.TopWindow);
                messageBox.SetTextTokens(guild.TokensWelcome(), guild);
                messageBox.ClickAnywhereToClose = true;
                messageBox.Show();
            }
        }

        #endregion

        #region Service Handling: Quests

        protected override void GetQuest()
        {
            // Just exit if this NPC already involved in an active quest
            // If quest conditions are complete the quest system should pickup ending
            if (QuestMachine.Instance.IsLastNPCClickedAnActiveQuestor(guild))
            {
                CloseWindow();
                return;
            }

            // Get member status, including temple specific statuses
            MembershipStatus status = guild.IsMember() ? MembershipStatus.Member : MembershipStatus.Nonmember;
            if (guild.IsMember() && guildGroup == FactionFile.GuildGroups.HolyOrder)
                status = (MembershipStatus) Enum.Parse(typeof(MembershipStatus), ((Temple)guild).Deity.ToString());

            // Get the faction id for affecting reputation on success/failure
            int factionId = (guildGroup == FactionFile.GuildGroups.HolyOrder || guildGroup == FactionFile.GuildGroups.KnightlyOrder) ? buildingFactionId : guildManager.GetGuildFactionId(guildGroup);

            // Select a quest at random from appropriate pool
            offeredQuest = GameManager.Instance.QuestListsManager.GetGuildQuest(guildGroup, status, factionId, guild.GetReputation(playerEntity), guild.Rank);
            if (offeredQuest != null)
            {
                // Log offered quest
                Debug.LogFormat("Offering quest {0} from Guild {1} affecting factionId {2}", offeredQuest.QuestName, guildGroup, offeredQuest.FactionId);

                // Offer the quest to player, setting external context provider to guild if a member
                if (guild.IsMember())
                    offeredQuest.ExternalMCP = guild;
                DaggerfallMessageBox messageBox = QuestMachine.Instance.CreateMessagePrompt(offeredQuest, (int)QuestMachine.QuestMessages.QuestorOffer);
                if (messageBox != null)
                {
                    messageBox.OnButtonClick += OfferQuest_OnButtonClick;
                    messageBox.Show();
                }
            }
            else
            {
                ShowFailGetQuestMessage();
            }
        }

        #endregion

        #region Service Handling: Training

        private List<DFCareer.Skills> GetTrainingSkills()
        {
            switch (npcService)
            {
                // Handle Temples even when not a member
                case GuildNpcServices.TAk_Training:
                    return Temple.GetTrainingSkills(Temple.Divines.Akatosh);
                case GuildNpcServices.TAr_Training:
                    return Temple.GetTrainingSkills(Temple.Divines.Arkay);
                case GuildNpcServices.TDi_Training:
                    return Temple.GetTrainingSkills(Temple.Divines.Dibella);
                case GuildNpcServices.TJu_Training:
                    return Temple.GetTrainingSkills(Temple.Divines.Julianos);
                case GuildNpcServices.TKy_Training:
                    return Temple.GetTrainingSkills(Temple.Divines.Kynareth);
                case GuildNpcServices.TMa_Training:
                    return Temple.GetTrainingSkills(Temple.Divines.Mara);
                case GuildNpcServices.TSt_Training:
                    return Temple.GetTrainingSkills(Temple.Divines.Stendarr);
                case GuildNpcServices.TZe_Training:
                    return Temple.GetTrainingSkills(Temple.Divines.Zenithar);
                default:
                    return guild.TrainingSkills;
            }
        }

        void TrainingService()
        {
            CloseWindow();
            // Check enough time has passed since last trained
            DaggerfallDateTime now = DaggerfallUnity.Instance.WorldTime.Now;
            if ((now.ToClassicDaggerfallTime() - playerEntity.TimeOfLastSkillTraining) < 720)
            {
                TextFile.Token[] tokens = DaggerfallUnity.Instance.TextProvider.GetRandomTokens(TrainingToSoonId);
                DaggerfallMessageBox messageBox = new DaggerfallMessageBox(uiManager, uiManager.TopWindow);
                messageBox.SetTextTokens(tokens);
                messageBox.ClickAnywhereToClose = true;
                messageBox.Show();
            }
            else
            {   // Offer training price
                DaggerfallMessageBox messageBox = new DaggerfallMessageBox(uiManager, uiManager.TopWindow);
                TextFile.Token[] tokens = DaggerfallUnity.Instance.TextProvider.GetRSCTokens(TrainingOfferId);
                messageBox.SetTextTokens(tokens, guild);
                messageBox.AddButton(DaggerfallMessageBox.MessageBoxButtons.Yes);
                messageBox.AddButton(DaggerfallMessageBox.MessageBoxButtons.No);
                messageBox.OnButtonClick += ConfirmTraining_OnButtonClick;
                messageBox.Show();
            }
        }

        private void ConfirmTraining_OnButtonClick(DaggerfallMessageBox sender, DaggerfallMessageBox.MessageBoxButtons messageBoxButton)
        {
            CloseWindow();
            if (messageBoxButton == DaggerfallMessageBox.MessageBoxButtons.Yes)
            {
                if (playerEntity.GetGoldAmount() >= guild.GetTrainingPrice())
                {
                    // Show skill picker loaded with guild training skills
                    DaggerfallListPickerWindow skillPicker = new DaggerfallListPickerWindow(uiManager, this);
                    skillPicker.OnItemPicked += TrainingSkill_OnItemPicked;

                    foreach (DFCareer.Skills skill in GetTrainingSkills())
                        skillPicker.ListBox.AddItem(DaggerfallUnity.Instance.TextProvider.GetSkillName(skill));

                    uiManager.PushWindow(skillPicker);
                }
                else
                    DaggerfallUI.MessageBox(NotEnoughGoldId);
            }
        }

        private void TrainingSkill_OnItemPicked(int index, string skillName)
        {
            CloseWindow();
            List<DFCareer.Skills> trainingSkills = GetTrainingSkills();
            DFCareer.Skills skillToTrain = trainingSkills[index];

            if (playerEntity.Skills.GetPermanentSkillValue(skillToTrain) > guild.GetTrainingMax(skillToTrain))
            {
                // Inform player they're too skilled to train
                TextFile.Token[] tokens = DaggerfallUnity.Instance.TextProvider.GetRandomTokens(TrainingTooSkilledId);
                DaggerfallMessageBox messageBox = new DaggerfallMessageBox(uiManager, uiManager.TopWindow);
                messageBox.SetTextTokens(tokens, guild);
                messageBox.ClickAnywhereToClose = true;
                messageBox.Show();
            }
            else
            {   // Train the skill
                DaggerfallDateTime now = DaggerfallUnity.Instance.WorldTime.Now;
                playerEntity.TimeOfLastSkillTraining = now.ToClassicDaggerfallTime();
                now.RaiseTime(DaggerfallDateTime.SecondsPerHour * 3);
                playerEntity.DeductGoldAmount(guild.GetTrainingPrice());
                playerEntity.DecreaseFatigue(PlayerEntity.DefaultFatigueLoss * 180);
                int skillAdvancementMultiplier = DaggerfallSkills.GetAdvancementMultiplier(skillToTrain);
                short tallyAmount = (short)(UnityEngine.Random.Range(10, 21) * skillAdvancementMultiplier);
                playerEntity.TallySkill(skillToTrain, tallyAmount);
                DaggerfallUI.MessageBox(TrainSkillId);
            }
        }

        #endregion

        #region Service Handling: Donation

        void DonationService()
        {
            CloseWindow();
            DaggerfallInputMessageBox donationMsgBox = new DaggerfallInputMessageBox(uiManager, this);
            donationMsgBox.SetTextBoxLabel(HardStrings.serviceDonateHowMuch);
            donationMsgBox.TextPanelDistanceX = 6;
            donationMsgBox.TextPanelDistanceY = 6;
            donationMsgBox.TextBox.Numeric = true;
            donationMsgBox.TextBox.MaxCharacters = 8;
            donationMsgBox.TextBox.Text = "1000";
            donationMsgBox.OnGotUserInput += DonationMsgBox_OnGotUserInput;
            donationMsgBox.Show();
        }

        private void DonationMsgBox_OnGotUserInput(DaggerfallInputMessageBox sender, string input)
        {
            int amount = 0;
            if (int.TryParse(input, out amount))
            {
                if (playerEntity.GetGoldAmount() > amount)
                {
                    // Deduct gold, and apply blessing if member
                    playerEntity.DeductGoldAmount(amount);
                    int factionId = (int)Temple.GetDivine(buildingFactionId);

                    // Change reputation
                    int rep = Math.Abs(playerEntity.FactionData.GetReputation(factionId));
                    if (UnityEngine.Random.Range(1, 101) <= (2 * amount / rep + 1))
                        playerEntity.FactionData.ChangeReputation(factionId, 1); // Does not propagate in classic

                    // Show thanks message
                    DaggerfallMessageBox messageBox = new DaggerfallMessageBox(uiManager, uiManager.TopWindow);
                    messageBox.SetTextTokens(DaggerfallUnity.Instance.TextProvider.GetRandomTokens(DonationThanksId), this);
                    messageBox.ClickAnywhereToClose = true;
                    messageBox.Show();
                }
                else
                {
                    DaggerfallMessageBox messageBox = new DaggerfallMessageBox(uiManager, uiManager.TopWindow);
                    messageBox.SetTextTokens(DaggerfallUnity.Instance.TextProvider.GetRandomTokens(TooGenerousId), this);
                    messageBox.ClickAnywhereToClose = true;
                    messageBox.Show();
                }
            }
        }

        #endregion

        #region Service Handling: CureDisease

        void CureDiseaseService()
        {
            CloseWindow();
            int numberOfDiseases = GameManager.Instance.PlayerEffectManager.DiseaseCount;

            if (playerEntity.TimeToBecomeVampireOrWerebeast != 0)
                numberOfDiseases++;

            if (numberOfDiseases > 0)
            {
                // Get base cost
                int baseCost = 250 * numberOfDiseases;

                // Apply rank-based discount if this is an Arkay temple
                baseCost = guild.ReducedCureCost(baseCost);

                // Apply temple quality and regional price modifiers
                int costBeforeBargaining = FormulaHelper.CalculateCost(baseCost, buildingDiscoveryData.quality);

                // Apply bargaining to get final price
                curingCost = FormulaHelper.CalculateTradePrice(costBeforeBargaining, buildingDiscoveryData.quality, false);

                // Index correct message
                const int tradeMessageBaseId = 260;
                int msgOffset = 0;
                if (costBeforeBargaining >> 1 <= curingCost)
                {
                    if (costBeforeBargaining - (costBeforeBargaining >> 2) <= curingCost)
                        msgOffset = 2;
                    else
                        msgOffset = 1;
                }
                // Offer curing at the calculated price.
                DaggerfallMessageBox messageBox = new DaggerfallMessageBox(uiManager, uiManager.TopWindow);
                TextFile.Token[] tokens = DaggerfallUnity.Instance.TextProvider.GetRandomTokens(tradeMessageBaseId + msgOffset);
                messageBox.SetTextTokens(tokens, this);
                messageBox.AddButton(DaggerfallMessageBox.MessageBoxButtons.Yes);
                messageBox.AddButton(DaggerfallMessageBox.MessageBoxButtons.No);
                messageBox.OnButtonClick += ConfirmCuring_OnButtonClick;
                messageBox.Show();
            }
            else
            {   // Not diseased
                DaggerfallUI.MessageBox(30);
            }
        }

        private void ConfirmCuring_OnButtonClick(DaggerfallMessageBox sender, DaggerfallMessageBox.MessageBoxButtons messageBoxButton)
        {
            CloseWindow();
            if (messageBoxButton == DaggerfallMessageBox.MessageBoxButtons.Yes)
            {
                if (playerEntity.GetGoldAmount() >= curingCost)
                {
                    playerEntity.DeductGoldAmount(curingCost);
                    GameManager.Instance.PlayerEffectManager.CureAllDiseases();
                    playerEntity.TimeToBecomeVampireOrWerebeast = 0;
                    DaggerfallUI.MessageBox(HardStrings.serviceCured);
                }
                else
                    DaggerfallUI.MessageBox(NotEnoughGoldId);
            }
        }

        #endregion

        #region Service Handling: Make Potions

        private void MakePotionService()
        {
            // Open potion mixer window if player has some ingredients
            CloseWindow();
            for (int i = 0; i < playerEntity.Items.Count; i++)
            {
                if (playerEntity.Items.GetItem(i).IsIngredient)
                {
                    uiManager.PushWindow(DaggerfallUI.Instance.DfPotionMakerWindow);
                    return;
                }
            }
            DaggerfallUI.MessageBox(34);
        }

        #endregion

        #region Service Handling: Receive Armor / House (Knightly Orders only)

        void ReceiveArmorService()
        {
            CloseWindow();
            KnightlyOrder order = (KnightlyOrder) guild;
            order.ReceiveArmor(playerEntity);
        }

        void ReceiveHouseService()
        {
            CloseWindow();
            KnightlyOrder order = (KnightlyOrder) guild;
            order.ReceiveHouse();
        }

        #endregion

        #region Service Handling: SpyMaster

        // Message box closed, talk to SpyMaster
        private void SpyMasterGreetingPopUp_OnClose()
        {
            GameManager.Instance.TalkManager.TalkToStaticNPC(QuestMachine.Instance.LastNPCClicked, true, true);
        }

        #endregion

        #region Macro handling

        public override MacroDataSource GetMacroDataSource()
        {
            return new GuildServiceMacroDataSource(this);
        }

        /// <summary>
        /// MacroDataSource context sensitive methods for guild services window.
        /// </summary>
        private class GuildServiceMacroDataSource : MacroDataSource
        {
            private DaggerfallGuildServicePopupWindow parent;
            public GuildServiceMacroDataSource(DaggerfallGuildServicePopupWindow guildServiceWindow)
            {
                this.parent = guildServiceWindow;
            }

            public override string Amount()
            {
                return parent.curingCost.ToString();
            }

            public override string ShopName()
            {
                return parent.buildingDiscoveryData.displayName;
            }

            public override string God()
            {
                return Temple.GetDivine(parent.buildingFactionId).ToString();
            }

            public override string GodDesc()
            {
                return Temple.GetDeityDesc((Temple.Divines) parent.buildingFactionId);
            }

            public override string Daedra()
            {
                FactionFile.FactionData factionData;
                if (GameManager.Instance.PlayerEntity.FactionData.GetFactionData(parent.daedraToSummon.factionId, out factionData))
                    return factionData.name;
                else
                    return "%dae[error]";
            }
        }

        #endregion

    }
}