namespace ChamberControlSimulator
{
    partial class Form1
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            components = new System.ComponentModel.Container();
            LayoutPanel = new TableLayoutPanel();
            pnlTopBar = new Panel();
            lblControllerState = new Label();
            lblTitle = new Label();
            pnlState = new Panel();
            grpEquipmentStatus = new GroupBox();
            prgTemperature = new ProgressBar();
            lblProgressStage = new Label();
            lblTargetTemp = new Label();
            lblCurrentTemp = new Label();
            lblEquipmentState = new Label();
            pnlRecipeCommand = new Panel();
            grpRecipeCommand = new GroupBox();
            btnStop = new Button();
            btnReset = new Button();
            btnAcknowledge = new Button();
            btnStart = new Button();
            cmbRecipe = new ComboBox();
            lblRecipeTargetTemp = new Label();
            lblRecipeText = new Label();
            pnlSafetyStatus = new Panel();
            grpSafetyInterlock = new GroupBox();
            lblActiveAlarm = new Label();
            lblRecoveryReady = new Label();
            lblFeedbackState = new Label();
            lblDoorState = new Label();
            lblPlcConnection = new Label();
            lblSynchronization = new Label();
            lblCommandStatus = new Label();
            pnlSimulation = new Panel();
            grpSimulationInput = new GroupBox();
            nudSimulatedTemperature = new NumericUpDown();
            btnApplyTemperature = new Button();
            btnResumeFeedback = new Button();
            btnPauseFeedback = new Button();
            btnDoorToggle = new Button();
            btnSuppressAck = new Button();
            btnForceDisconnect = new Button();
            lblSimulationFeedbackText = new Label();
            lblSimulationTempText = new Label();
            lblSimulationDoorText = new Label();
            pnlEventLog = new Panel();
            grpEventLog = new GroupBox();
            lvwEventLog = new ListView();
            colLogTime = new ColumnHeader();
            colLogState = new ColumnHeader();
            colLogEvent = new ColumnHeader();
            colLogAlarm = new ColumnHeader();
            tmSimulationTick = new System.Windows.Forms.Timer(components);
            LayoutPanel.SuspendLayout();
            pnlTopBar.SuspendLayout();
            pnlState.SuspendLayout();
            grpEquipmentStatus.SuspendLayout();
            pnlRecipeCommand.SuspendLayout();
            grpRecipeCommand.SuspendLayout();
            pnlSafetyStatus.SuspendLayout();
            grpSafetyInterlock.SuspendLayout();
            pnlSimulation.SuspendLayout();
            grpSimulationInput.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)nudSimulatedTemperature).BeginInit();
            pnlEventLog.SuspendLayout();
            grpEventLog.SuspendLayout();
            SuspendLayout();
            //
            // LayoutPanel
            //
            LayoutPanel.ColumnCount = 2;
            LayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            LayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            LayoutPanel.Controls.Add(pnlTopBar, 0, 0);
            LayoutPanel.Controls.Add(pnlState, 0, 1);
            LayoutPanel.Controls.Add(pnlRecipeCommand, 1, 1);
            LayoutPanel.Controls.Add(pnlSafetyStatus, 0, 2);
            LayoutPanel.Controls.Add(pnlSimulation, 1, 2);
            LayoutPanel.Controls.Add(pnlEventLog, 0, 3);
            LayoutPanel.Dock = DockStyle.Fill;
            LayoutPanel.Location = new Point(0, 0);
            LayoutPanel.Margin = new Padding(6);
            LayoutPanel.Name = "LayoutPanel";
            LayoutPanel.RowCount = 4;
            LayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 107F));
            LayoutPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 53.5087738F));
            LayoutPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 46.4912262F));
            LayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 546F));
            LayoutPanel.Size = new Size(1288, 1385);
            LayoutPanel.TabIndex = 0;
            LayoutPanel.Paint += tableLayoutPanel1_Paint;
            //
            // pnlTopBar
            //
            LayoutPanel.SetColumnSpan(pnlTopBar, 2);
            pnlTopBar.Controls.Add(lblControllerState);
            pnlTopBar.Controls.Add(lblTitle);
            pnlTopBar.Dock = DockStyle.Fill;
            pnlTopBar.Location = new Point(6, 6);
            pnlTopBar.Margin = new Padding(6);
            pnlTopBar.Name = "pnlTopBar";
            pnlTopBar.Padding = new Padding(24, 17, 24, 17);
            pnlTopBar.Size = new Size(1276, 95);
            pnlTopBar.TabIndex = 0;
            //
            // lblControllerState
            //
            lblControllerState.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            lblControllerState.BackColor = Color.FromArgb(227, 242, 253);
            lblControllerState.BorderStyle = BorderStyle.FixedSingle;
            lblControllerState.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            lblControllerState.ForeColor = Color.FromArgb(21, 101, 192);
            lblControllerState.Location = new Point(1080, 21);
            lblControllerState.Margin = new Padding(6, 0, 6, 0);
            lblControllerState.Name = "lblControllerState";
            lblControllerState.Size = new Size(170, 51);
            lblControllerState.TabIndex = 1;
            lblControllerState.Text = "IDLE";
            lblControllerState.TextAlign = ContentAlignment.MiddleCenter;
            //
            // lblTitle
            //
            lblTitle.AutoSize = true;
            lblTitle.Font = new Font("Segoe UI", 12F, FontStyle.Bold);
            lblTitle.ForeColor = Color.FromArgb(30, 58, 95);
            lblTitle.Location = new Point(32, 26);
            lblTitle.Margin = new Padding(6, 0, 6, 0);
            lblTitle.Name = "lblTitle";
            lblTitle.Size = new Size(640, 45);
            lblTitle.TabIndex = 0;
            lblTitle.Text = "Virtual Thermal Chamber Control";
            //
            // pnlState
            //
            pnlState.Controls.Add(grpEquipmentStatus);
            pnlState.Dock = DockStyle.Fill;
            pnlState.Location = new Point(6, 113);
            pnlState.Margin = new Padding(6);
            pnlState.Name = "pnlState";
            pnlState.Padding = new Padding(16, 17, 16, 17);
            pnlState.Size = new Size(632, 379);
            pnlState.TabIndex = 1;
            //
            // grpEquipmentStatus
            //
            grpEquipmentStatus.Controls.Add(prgTemperature);
            grpEquipmentStatus.Controls.Add(lblProgressStage);
            grpEquipmentStatus.Controls.Add(lblTargetTemp);
            grpEquipmentStatus.Controls.Add(lblCurrentTemp);
            grpEquipmentStatus.Controls.Add(lblEquipmentState);
            grpEquipmentStatus.Dock = DockStyle.Fill;
            grpEquipmentStatus.Location = new Point(16, 17);
            grpEquipmentStatus.Margin = new Padding(16, 17, 16, 17);
            grpEquipmentStatus.Name = "grpEquipmentStatus";
            grpEquipmentStatus.Padding = new Padding(24, 26, 24, 26);
            grpEquipmentStatus.Size = new Size(600, 345);
            grpEquipmentStatus.TabIndex = 0;
            grpEquipmentStatus.TabStop = false;
            grpEquipmentStatus.Text = "현재 장비 상태";
            grpEquipmentStatus.Enter += grpEquipmentStatus_Enter;
            //
            // prgTemperature
            //
            prgTemperature.Location = new Point(36, 273);
            prgTemperature.Margin = new Padding(6);
            prgTemperature.Name = "prgTemperature";
            prgTemperature.Size = new Size(488, 43);
            prgTemperature.TabIndex = 4;
            //
            // lblProgressStage
            //
            lblProgressStage.AutoSize = true;
            lblProgressStage.Location = new Point(36, 220);
            lblProgressStage.Margin = new Padding(6, 0, 6, 0);
            lblProgressStage.Name = "lblProgressStage";
            lblProgressStage.Size = new Size(164, 32);
            lblProgressStage.TabIndex = 3;
            lblProgressStage.Text = "진행 단계 : —";
            //
            // lblTargetTemp
            //
            lblTargetTemp.AutoSize = true;
            lblTargetTemp.Location = new Point(36, 166);
            lblTargetTemp.Margin = new Padding(6, 0, 6, 0);
            lblTargetTemp.Name = "lblTargetTemp";
            lblTargetTemp.Size = new Size(195, 32);
            lblTargetTemp.TabIndex = 2;
            lblTargetTemp.Text = "목표 온도 : — ℃";
            //
            // lblCurrentTemp
            //
            lblCurrentTemp.AutoSize = true;
            lblCurrentTemp.Location = new Point(36, 113);
            lblCurrentTemp.Margin = new Padding(6, 0, 6, 0);
            lblCurrentTemp.Name = "lblCurrentTemp";
            lblCurrentTemp.Size = new Size(195, 32);
            lblCurrentTemp.TabIndex = 1;
            lblCurrentTemp.Text = "현재 온도 : — ℃";
            //
            // lblEquipmentState
            //
            lblEquipmentState.AutoSize = true;
            lblEquipmentState.Location = new Point(36, 60);
            lblEquipmentState.Margin = new Padding(6, 0, 6, 0);
            lblEquipmentState.Name = "lblEquipmentState";
            lblEquipmentState.Size = new Size(108, 32);
            lblEquipmentState.TabIndex = 0;
            lblEquipmentState.Text = "상태 : —";
            //
            // pnlRecipeCommand
            //
            pnlRecipeCommand.Controls.Add(grpRecipeCommand);
            pnlRecipeCommand.Dock = DockStyle.Fill;
            pnlRecipeCommand.Location = new Point(650, 113);
            pnlRecipeCommand.Margin = new Padding(6);
            pnlRecipeCommand.Name = "pnlRecipeCommand";
            pnlRecipeCommand.Padding = new Padding(16, 17, 16, 17);
            pnlRecipeCommand.Size = new Size(632, 379);
            pnlRecipeCommand.TabIndex = 2;
            //
            // grpRecipeCommand
            //
            grpRecipeCommand.Controls.Add(btnStop);
            grpRecipeCommand.Controls.Add(btnReset);
            grpRecipeCommand.Controls.Add(btnAcknowledge);
            grpRecipeCommand.Controls.Add(btnStart);
            grpRecipeCommand.Controls.Add(cmbRecipe);
            grpRecipeCommand.Controls.Add(lblRecipeTargetTemp);
            grpRecipeCommand.Controls.Add(lblRecipeText);
            grpRecipeCommand.Dock = DockStyle.Fill;
            grpRecipeCommand.Location = new Point(16, 17);
            grpRecipeCommand.Margin = new Padding(16, 17, 16, 17);
            grpRecipeCommand.Name = "grpRecipeCommand";
            grpRecipeCommand.Padding = new Padding(24, 26, 24, 26);
            grpRecipeCommand.Size = new Size(600, 345);
            grpRecipeCommand.TabIndex = 0;
            grpRecipeCommand.TabStop = false;
            grpRecipeCommand.Text = "Recipe / Command";
            //
            // btnStop
            //
            btnStop.Location = new Point(316, 201);
            btnStop.Margin = new Padding(6);
            btnStop.Name = "btnStop";
            btnStop.Size = new Size(188, 51);
            btnStop.TabIndex = 4;
            btnStop.Text = "Stop";
            btnStop.UseVisualStyleBackColor = true;
            btnStop.Click += btnStop_Click;
            //
            // btnReset
            //
            btnReset.Location = new Point(316, 269);
            btnReset.Margin = new Padding(6);
            btnReset.Name = "btnReset";
            btnReset.Size = new Size(188, 51);
            btnReset.TabIndex = 6;
            btnReset.Text = "Reset";
            btnReset.UseVisualStyleBackColor = true;
            btnReset.Click += btnReset_Click;
            //
            // btnAcknowledge
            //
            btnAcknowledge.Location = new Point(96, 269);
            btnAcknowledge.Margin = new Padding(6);
            btnAcknowledge.Name = "btnAcknowledge";
            btnAcknowledge.Size = new Size(188, 51);
            btnAcknowledge.TabIndex = 5;
            btnAcknowledge.Text = "Acknowledge";
            btnAcknowledge.UseVisualStyleBackColor = true;
            btnAcknowledge.Click += btnAcknowledge_Click;
            //
            // btnStart
            //
            btnStart.Location = new Point(96, 201);
            btnStart.Margin = new Padding(6);
            btnStart.Name = "btnStart";
            btnStart.Size = new Size(188, 51);
            btnStart.TabIndex = 3;
            btnStart.Text = "Start";
            btnStart.UseVisualStyleBackColor = true;
            btnStart.Click += btnStart_Click;
            //
            // cmbRecipe
            //
            cmbRecipe.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbRecipe.FormattingEnabled = true;
            cmbRecipe.Location = new Point(164, 64);
            cmbRecipe.Margin = new Padding(6);
            cmbRecipe.Name = "cmbRecipe";
            cmbRecipe.Size = new Size(304, 40);
            cmbRecipe.TabIndex = 1;
            //
            // lblRecipeTargetTemp
            //
            lblRecipeTargetTemp.AutoSize = true;
            lblRecipeTargetTemp.Location = new Point(36, 132);
            lblRecipeTargetTemp.Margin = new Padding(6, 0, 6, 0);
            lblRecipeTargetTemp.Name = "lblRecipeTargetTemp";
            lblRecipeTargetTemp.Size = new Size(128, 32);
            lblRecipeTargetTemp.TabIndex = 2;
            lblRecipeTargetTemp.Text = "Target : —";
            //
            // lblRecipeText
            //
            lblRecipeText.AutoSize = true;
            lblRecipeText.Location = new Point(36, 70);
            lblRecipeText.Margin = new Padding(6, 0, 6, 0);
            lblRecipeText.Name = "lblRecipeText";
            lblRecipeText.Size = new Size(99, 32);
            lblRecipeText.TabIndex = 0;
            lblRecipeText.Text = "Recipe :";
            //
            // pnlSafetyStatus
            //
            pnlSafetyStatus.Controls.Add(grpSafetyInterlock);
            pnlSafetyStatus.Dock = DockStyle.Fill;
            pnlSafetyStatus.Location = new Point(6, 504);
            pnlSafetyStatus.Margin = new Padding(6);
            pnlSafetyStatus.Name = "pnlSafetyStatus";
            pnlSafetyStatus.Padding = new Padding(16, 17, 16, 17);
            pnlSafetyStatus.Size = new Size(632, 328);
            pnlSafetyStatus.TabIndex = 3;
            //
            // grpSafetyInterlock
            //
            grpSafetyInterlock.Controls.Add(lblCommandStatus);
            grpSafetyInterlock.Controls.Add(lblSynchronization);
            grpSafetyInterlock.Controls.Add(lblPlcConnection);
            grpSafetyInterlock.Controls.Add(lblActiveAlarm);
            grpSafetyInterlock.Controls.Add(lblRecoveryReady);
            grpSafetyInterlock.Controls.Add(lblFeedbackState);
            grpSafetyInterlock.Controls.Add(lblDoorState);
            grpSafetyInterlock.Dock = DockStyle.Fill;
            grpSafetyInterlock.Location = new Point(16, 17);
            grpSafetyInterlock.Margin = new Padding(16, 17, 16, 17);
            grpSafetyInterlock.Name = "grpSafetyInterlock";
            grpSafetyInterlock.Padding = new Padding(24, 26, 24, 26);
            grpSafetyInterlock.Size = new Size(600, 294);
            grpSafetyInterlock.TabIndex = 0;
            grpSafetyInterlock.TabStop = false;
            grpSafetyInterlock.Text = "Safety / Interlock";
            //
            // lblActiveAlarm
            //
            lblActiveAlarm.AutoSize = true;
            lblActiveAlarm.Location = new Point(36, 274);
            lblActiveAlarm.Margin = new Padding(6, 0, 6, 0);
            lblActiveAlarm.Name = "lblActiveAlarm";
            lblActiveAlarm.Size = new Size(197, 32);
            lblActiveAlarm.TabIndex = 5;
            lblActiveAlarm.Text = "Active Alarm : —";
            //
            // lblRecoveryReady
            //
            lblRecoveryReady.AutoSize = true;
            lblRecoveryReady.Location = new Point(36, 328);
            lblRecoveryReady.Margin = new Padding(6, 0, 6, 0);
            lblRecoveryReady.Name = "lblRecoveryReady";
            lblRecoveryReady.Size = new Size(232, 32);
            lblRecoveryReady.TabIndex = 6;
            lblRecoveryReady.Text = "Recovery Ready : —";
            //
            // lblFeedbackState
            //
            lblFeedbackState.AutoSize = true;
            lblFeedbackState.Location = new Point(36, 220);
            lblFeedbackState.Margin = new Padding(6, 0, 6, 0);
            lblFeedbackState.Name = "lblFeedbackState";
            lblFeedbackState.Size = new Size(241, 32);
            lblFeedbackState.TabIndex = 1;
            lblFeedbackState.Text = "Sensor Feedback : —";
            //
            // lblDoorState
            //
            lblDoorState.AutoSize = true;
            lblDoorState.Location = new Point(36, 180);
            lblDoorState.Margin = new Padding(6, 0, 6, 0);
            lblDoorState.Name = "lblDoorState";
            lblDoorState.Size = new Size(113, 32);
            lblDoorState.TabIndex = 0;
            lblDoorState.Text = "Door : —";
            //
            // lblPlcConnection
            //
            lblPlcConnection.AutoSize = true;
            lblPlcConnection.Location = new Point(36, 60);
            lblPlcConnection.Margin = new Padding(6, 0, 6, 0);
            lblPlcConnection.Name = "lblPlcConnection";
            lblPlcConnection.Size = new Size(260, 32);
            lblPlcConnection.TabIndex = 7;
            lblPlcConnection.Text = "PLC Connection : —";
            //
            // lblSynchronization
            //
            lblSynchronization.AutoSize = true;
            lblSynchronization.Location = new Point(36, 100);
            lblSynchronization.Margin = new Padding(6, 0, 6, 0);
            lblSynchronization.Name = "lblSynchronization";
            lblSynchronization.Size = new Size(260, 32);
            lblSynchronization.TabIndex = 8;
            lblSynchronization.Text = "Synchronization : —";
            //
            // lblCommandStatus
            //
            lblCommandStatus.AutoSize = true;
            lblCommandStatus.Location = new Point(36, 140);
            lblCommandStatus.Margin = new Padding(6, 0, 6, 0);
            lblCommandStatus.Name = "lblCommandStatus";
            lblCommandStatus.Size = new Size(260, 32);
            lblCommandStatus.TabIndex = 9;
            lblCommandStatus.Text = "Command : None";
            //
            // pnlSimulation
            //
            pnlSimulation.Controls.Add(grpSimulationInput);
            pnlSimulation.Dock = DockStyle.Fill;
            pnlSimulation.Location = new Point(650, 504);
            pnlSimulation.Margin = new Padding(6);
            pnlSimulation.Name = "pnlSimulation";
            pnlSimulation.Padding = new Padding(16, 17, 16, 17);
            pnlSimulation.Size = new Size(632, 328);
            pnlSimulation.TabIndex = 4;
            //
            // grpSimulationInput
            //
            grpSimulationInput.Controls.Add(nudSimulatedTemperature);
            grpSimulationInput.Controls.Add(btnApplyTemperature);
            grpSimulationInput.Controls.Add(btnResumeFeedback);
            grpSimulationInput.Controls.Add(btnPauseFeedback);
            grpSimulationInput.Controls.Add(btnForceDisconnect);
            grpSimulationInput.Controls.Add(btnSuppressAck);
            grpSimulationInput.Controls.Add(btnDoorToggle);
            grpSimulationInput.Controls.Add(lblSimulationFeedbackText);
            grpSimulationInput.Controls.Add(lblSimulationTempText);
            grpSimulationInput.Controls.Add(lblSimulationDoorText);
            grpSimulationInput.Dock = DockStyle.Fill;
            grpSimulationInput.Location = new Point(16, 17);
            grpSimulationInput.Margin = new Padding(16, 17, 16, 17);
            grpSimulationInput.Name = "grpSimulationInput";
            grpSimulationInput.Padding = new Padding(24, 26, 24, 26);
            grpSimulationInput.Size = new Size(600, 294);
            grpSimulationInput.TabIndex = 0;
            grpSimulationInput.TabStop = false;
            grpSimulationInput.Text = "Simulation / Fault Injection";
            //
            // nudSimulatedTemperature
            //
            nudSimulatedTemperature.DecimalPlaces = 1;
            nudSimulatedTemperature.Increment = new decimal(new int[] { 5, 0, 0, 65536 });
            nudSimulatedTemperature.Location = new Point(210, 109);
            nudSimulatedTemperature.Margin = new Padding(6);
            nudSimulatedTemperature.Maximum = new decimal(new int[] { 500, 0, 0, 0 });
            nudSimulatedTemperature.Name = "nudSimulatedTemperature";
            nudSimulatedTemperature.Size = new Size(150, 39);
            nudSimulatedTemperature.TabIndex = 1;
            nudSimulatedTemperature.Value = new decimal(new int[] { 20, 0, 0, 0 });
            //
            // btnApplyTemperature
            //
            btnApplyTemperature.Location = new Point(372, 109);
            btnApplyTemperature.Margin = new Padding(6);
            btnApplyTemperature.Name = "btnApplyTemperature";
            btnApplyTemperature.Size = new Size(150, 49);
            btnApplyTemperature.TabIndex = 2;
            btnApplyTemperature.Text = "Apply";
            btnApplyTemperature.UseVisualStyleBackColor = true;
            btnApplyTemperature.Click += btnApplyTemperature_Click;
            //
            // btnResumeFeedback
            //
            btnResumeFeedback.Location = new Point(372, 166);
            btnResumeFeedback.Margin = new Padding(6);
            btnResumeFeedback.Name = "btnResumeFeedback";
            btnResumeFeedback.Size = new Size(150, 49);
            btnResumeFeedback.TabIndex = 4;
            btnResumeFeedback.Text = "Resume";
            btnResumeFeedback.UseVisualStyleBackColor = true;
            btnResumeFeedback.Click += btnResumeFeedback_Click;
            //
            // btnPauseFeedback
            //
            btnPauseFeedback.Location = new Point(210, 166);
            btnPauseFeedback.Margin = new Padding(6);
            btnPauseFeedback.Name = "btnPauseFeedback";
            btnPauseFeedback.Size = new Size(150, 49);
            btnPauseFeedback.TabIndex = 3;
            btnPauseFeedback.Text = "Pause";
            btnPauseFeedback.UseVisualStyleBackColor = true;
            btnPauseFeedback.Click += btnPauseFeedback_Click;
            //
            // btnDoorToggle
            //
            btnDoorToggle.Location = new Point(210, 47);
            btnDoorToggle.Margin = new Padding(6);
            btnDoorToggle.Name = "btnDoorToggle";
            btnDoorToggle.Size = new Size(150, 49);
            btnDoorToggle.TabIndex = 0;
            btnDoorToggle.Text = "Open Door";
            btnDoorToggle.UseVisualStyleBackColor = true;
            btnDoorToggle.Click += btnDoorToggle_Click;
            //
            // btnSuppressAck
            //
            btnSuppressAck.Location = new Point(210, 223);
            btnSuppressAck.Margin = new Padding(6);
            btnSuppressAck.Name = "btnSuppressAck";
            btnSuppressAck.Size = new Size(150, 49);
            btnSuppressAck.TabIndex = 5;
            btnSuppressAck.Text = "Suppress ACK";
            btnSuppressAck.UseVisualStyleBackColor = true;
            btnSuppressAck.Click += btnSuppressAck_Click;
            //
            // btnForceDisconnect
            //
            btnForceDisconnect.Location = new Point(372, 223);
            btnForceDisconnect.Margin = new Padding(6);
            btnForceDisconnect.Name = "btnForceDisconnect";
            btnForceDisconnect.Size = new Size(150, 49);
            btnForceDisconnect.TabIndex = 6;
            btnForceDisconnect.Text = "Disconnect";
            btnForceDisconnect.UseVisualStyleBackColor = true;
            btnForceDisconnect.Click += btnForceDisconnect_Click;
            //
            // lblSimulationFeedbackText
            //
            lblSimulationFeedbackText.AutoSize = true;
            lblSimulationFeedbackText.Location = new Point(36, 175);
            lblSimulationFeedbackText.Margin = new Padding(6, 0, 6, 0);
            lblSimulationFeedbackText.Name = "lblSimulationFeedbackText";
            lblSimulationFeedbackText.Size = new Size(128, 32);
            lblSimulationFeedbackText.TabIndex = 2;
            lblSimulationFeedbackText.Text = "Feedback :";
            //
            // lblSimulationTempText
            //
            lblSimulationTempText.AutoSize = true;
            lblSimulationTempText.Location = new Point(36, 113);
            lblSimulationTempText.Margin = new Padding(6, 0, 6, 0);
            lblSimulationTempText.Name = "lblSimulationTempText";
            lblSimulationTempText.Size = new Size(164, 32);
            lblSimulationTempText.TabIndex = 1;
            lblSimulationTempText.Text = "Temperature :";
            //
            // lblSimulationDoorText
            //
            lblSimulationDoorText.AutoSize = true;
            lblSimulationDoorText.Location = new Point(36, 60);
            lblSimulationDoorText.Margin = new Padding(6, 0, 6, 0);
            lblSimulationDoorText.Name = "lblSimulationDoorText";
            lblSimulationDoorText.Size = new Size(80, 32);
            lblSimulationDoorText.TabIndex = 0;
            lblSimulationDoorText.Text = "Door :";
            //
            // pnlEventLog
            //
            LayoutPanel.SetColumnSpan(pnlEventLog, 2);
            pnlEventLog.Controls.Add(grpEventLog);
            pnlEventLog.Dock = DockStyle.Fill;
            pnlEventLog.Location = new Point(6, 844);
            pnlEventLog.Margin = new Padding(6);
            pnlEventLog.Name = "pnlEventLog";
            pnlEventLog.Padding = new Padding(16, 17, 16, 17);
            pnlEventLog.Size = new Size(1276, 535);
            pnlEventLog.TabIndex = 5;
            //
            // grpEventLog
            //
            grpEventLog.Controls.Add(lvwEventLog);
            grpEventLog.Dock = DockStyle.Fill;
            grpEventLog.Location = new Point(16, 17);
            grpEventLog.Margin = new Padding(16, 17, 16, 17);
            grpEventLog.Name = "grpEventLog";
            grpEventLog.Padding = new Padding(24, 26, 24, 26);
            grpEventLog.Size = new Size(1244, 501);
            grpEventLog.TabIndex = 0;
            grpEventLog.TabStop = false;
            grpEventLog.Text = "Event / Alarm Log";
            //
            // lvwEventLog
            //
            lvwEventLog.Columns.AddRange(new ColumnHeader[] { colLogTime, colLogState, colLogEvent, colLogAlarm });
            lvwEventLog.Dock = DockStyle.Fill;
            lvwEventLog.FullRowSelect = true;
            lvwEventLog.GridLines = true;
            lvwEventLog.Location = new Point(24, 58);
            lvwEventLog.Margin = new Padding(6);
            lvwEventLog.Name = "lvwEventLog";
            lvwEventLog.Size = new Size(1196, 417);
            lvwEventLog.TabIndex = 0;
            lvwEventLog.UseCompatibleStateImageBehavior = false;
            lvwEventLog.View = View.Details;
            //
            // colLogTime
            //
            colLogTime.Text = "Time";
            colLogTime.Width = 90;
            //
            // colLogState
            //
            colLogState.Text = "State";
            colLogState.Width = 100;
            //
            // colLogEvent
            //
            colLogEvent.Text = "Event";
            colLogEvent.Width = 300;
            //
            // colLogAlarm
            //
            colLogAlarm.Text = "Alarm";
            colLogAlarm.Width = 110;
            //
            // tmSimulationTick
            //
            tmSimulationTick.Interval = 250;
            tmSimulationTick.Tick += tmSimulationTick_Tick;
            //
            // Form1
            //
            AutoScaleDimensions = new SizeF(14F, 32F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(1288, 1385);
            Controls.Add(LayoutPanel);
            Margin = new Padding(4, 2, 4, 2);
            Name = "Form1";
            Text = "Chamber Control Simulator";
            Load += Form1_Load;
            LayoutPanel.ResumeLayout(false);
            pnlTopBar.ResumeLayout(false);
            pnlTopBar.PerformLayout();
            pnlState.ResumeLayout(false);
            grpEquipmentStatus.ResumeLayout(false);
            grpEquipmentStatus.PerformLayout();
            pnlRecipeCommand.ResumeLayout(false);
            grpRecipeCommand.ResumeLayout(false);
            grpRecipeCommand.PerformLayout();
            pnlSafetyStatus.ResumeLayout(false);
            grpSafetyInterlock.ResumeLayout(false);
            grpSafetyInterlock.PerformLayout();
            pnlSimulation.ResumeLayout(false);
            grpSimulationInput.ResumeLayout(false);
            grpSimulationInput.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)nudSimulatedTemperature).EndInit();
            pnlEventLog.ResumeLayout(false);
            grpEventLog.ResumeLayout(false);
            ResumeLayout(false);
        }

        #endregion

        private TableLayoutPanel LayoutPanel;
        private Panel pnlState;
        private Panel pnlRecipeCommand;
        private Panel pnlSafetyStatus;
        private Panel pnlSimulation;
        private Panel pnlEventLog;
        private Panel pnlTopBar;
        private GroupBox grpEquipmentStatus;
        private GroupBox grpRecipeCommand;
        private GroupBox grpSafetyInterlock;
        private GroupBox grpSimulationInput;
        private GroupBox grpEventLog;
        private Label lblControllerState;
        private Label lblTitle;
        private Label lblProgressStage;
        private Label lblTargetTemp;
        private Label lblCurrentTemp;
        private Label lblEquipmentState;
        private ProgressBar prgTemperature;
        private Label lblRecipeTargetTemp;
        private Label lblRecipeText;
        private ComboBox cmbRecipe;
        private Button btnStop;
        private Button btnReset;
        private Button btnAcknowledge;
        private Button btnStart;
        private Label lblPlcConnection;
        private Label lblSynchronization;
        private Label lblCommandStatus;
        private Label lblActiveAlarm;
        private Label lblRecoveryReady;
        private Label lblFeedbackState;
        private Label lblDoorState;
        private Label lblSimulationFeedbackText;
        private Label lblSimulationTempText;
        private Label lblSimulationDoorText;
        private ListView lvwEventLog;
        private ColumnHeader colLogTime;
        private ColumnHeader colLogState;
        private ColumnHeader colLogEvent;
        private ColumnHeader colLogAlarm;
        private Button btnPauseFeedback;
        private Button btnDoorToggle;
        private Button btnSuppressAck;
        private Button btnForceDisconnect;
        private NumericUpDown nudSimulatedTemperature;
        private Button btnResumeFeedback;
        private Button btnApplyTemperature;
        private System.Windows.Forms.Timer tmSimulationTick;
    }
}
