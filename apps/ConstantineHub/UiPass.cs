using ConstantineHub.Core;

namespace ConstantineHub;

internal static class UiPass
{
    private static readonly Color Background = Color.FromArgb(12, 18, 24);
    private static readonly Color Surface = Color.FromArgb(18, 26, 34);
    private static readonly Color SurfaceRaised = Color.FromArgb(23, 33, 43);
    private static readonly Color SurfaceHover = Color.FromArgb(31, 43, 55);
    private static readonly Color Border = Color.FromArgb(49, 62, 74);
    private static readonly Color TextPrimary = Color.FromArgb(236, 239, 242);
    private static readonly Color TextSecondary = Color.FromArgb(166, 177, 188);
    private static readonly Color Accent = Color.FromArgb(225, 145, 57);
    private static readonly Color AccentHover = Color.FromArgb(242, 165, 75);
    private static readonly Color Good = Color.FromArgb(92, 201, 120);
    private static readonly Color Warn = Color.FromArgb(232, 188, 104);
    private static readonly Color Bad = Color.FromArgb(232, 104, 104);

    internal static void Apply(MainForm form)
    {
        var legacyRoot = form.Controls
            .OfType<TableLayoutPanel>()
            .FirstOrDefault(panel => panel.ColumnCount == 1 && panel.RowCount >= 6);

        if (legacyRoot is null)
            return;

        var localCard = legacyRoot.GetControlFromPosition(0, 1);
        var blenderCard = legacyRoot.GetControlFromPosition(0, 2);
        var godotCard = legacyRoot.GetControlFromPosition(0, 3);
        var globalActions = legacyRoot.GetControlFromPosition(0, 4) as FlowLayoutPanel;
        var log = legacyRoot.GetControlFromPosition(0, 5) as RichTextBox;

        if (localCard is null || blenderCard is null || godotCard is null ||
            globalActions is null || log is null)
            return;

        legacyRoot.Controls.Remove(localCard);
        legacyRoot.Controls.Remove(blenderCard);
        legacyRoot.Controls.Remove(godotCard);
        legacyRoot.Controls.Remove(globalActions);
        legacyRoot.Controls.Remove(log);
        form.Controls.Remove(legacyRoot);
        legacyRoot.Dispose();

        form.SuspendLayout();
        form.MinimumSize = new Size(Scale(form, 1120), Scale(form, 720));
        form.Size = new Size(Scale(form, 1400), Scale(form, 900));
        form.BackColor = Background;
        form.ForeColor = TextPrimary;
        form.Font = new Font("Segoe UI", 10.5F);
        form.Padding = Padding.Empty;

        var shell = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Background,
            ColumnCount = 3,
            RowCount = 2,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Scale(form, 192)));
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Scale(form, 272)));
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, Scale(form, 72)));
        shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var adapterCards = new List<AdapterCardHost>();
        var uiTimer = new System.Windows.Forms.Timer { Interval = 1200 };
        var toolTip = new ToolTip
        {
            AutoPopDelay = 12000,
            InitialDelay = 500,
            ReshowDelay = 200
        };

        var currentProjectTop = MakeTextLabel("Profile: Local workstation  •  Project: checking…", 10.0F, TextSecondary);
        currentProjectTop.AutoEllipsis = true;
        var updateTop = MakeTextLabel("Stable • auto-check enabled", 9.5F, TextSecondary);
        updateTop.TextAlign = ContentAlignment.MiddleRight;
        updateTop.AutoEllipsis = true;

        var healthRail = MakeTextLabel("Checking services…", 12.5F, TextPrimary, bold: true);
        healthRail.AutoEllipsis = true;
        var healthDetail = MakeTextLabel("Waiting for adapter status", 9.0F, TextSecondary);
        healthDetail.AutoEllipsis = true;
        var updateRail = MakeTextLabel("Auto-check enabled", 10.0F, TextSecondary);
        updateRail.AutoEllipsis = true;
        var projectRail = MakeTextLabel("Checking project…", 9.5F, TextSecondary);
        projectRail.AutoEllipsis = true;

        var dashboardFlow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            BackColor = Background,
            Padding = new Padding(Scale(form, 16), Scale(form, 12), Scale(form, 10), Scale(form, 16)),
            Margin = Padding.Empty
        };

        var welcome = BuildWelcomeCard(form);
        dashboardFlow.Controls.Add(welcome);

        var localHost = BuildAdapterHost(
            form,
            localCard,
            "LOCAL FILES MCP",
            "LF",
            Accent,
            0,
            toolTip);
        var blenderHost = BuildAdapterHost(
            form,
            blenderCard,
            "BLENDER MCP",
            "B",
            Color.FromArgb(229, 126, 34),
            2,
            toolTip);
        var godotHost = BuildAdapterHost(
            form,
            godotCard,
            "GODOT MCP",
            "G",
            Color.FromArgb(77, 156, 217),
            4,
            toolTip);

        adapterCards.Add(localHost);
        adapterCards.Add(blenderHost);
        adapterCards.Add(godotHost);
        dashboardFlow.Controls.Add(localHost.Host);
        dashboardFlow.Controls.Add(blenderHost.Host);
        dashboardFlow.Controls.Add(godotHost.Host);

        var logHost = BuildLogPanel(form, log, out var setLogExpanded);
        dashboardFlow.Controls.Add(logHost);

        void ResizeDashboardChildren()
        {
            var width = Math.Max(
                Scale(form, 520),
                dashboardFlow.ClientSize.Width - dashboardFlow.Padding.Horizontal -
                (dashboardFlow.VerticalScroll.Visible ? SystemInformation.VerticalScrollBarWidth : 0) - Scale(form, 6));

            foreach (Control child in dashboardFlow.Controls)
                child.Width = width;
        }

        dashboardFlow.SizeChanged += (_, _) => ResizeDashboardChildren();
        dashboardFlow.ControlAdded += (_, _) => ResizeDashboardChildren();

        var localSettingsButton = FindButton(localCard, "Settings");
        var settingsMenu = BuildSettingsMenu(form, localSettingsButton,
            FindButton(globalActions, "Check Updates"), FindButton(globalActions, "Save Log"));
        var navigation = BuildNavigation(
            form,
            onHome: () =>
            {
                setLogExpanded(false);
                dashboardFlow.AutoScrollPosition = Point.Empty;
            },
            onDiagnostics: () =>
            {
                setLogExpanded(true);
                dashboardFlow.ScrollControlIntoView(logHost);
                log.Focus();
            },
            onUpdates: () => MainForm.InvokeButtonAction(FindButton(globalActions, "Check Updates")),
            onSettings: () => MainForm.InvokeButtonAction(localSettingsButton));

        var topBar = BuildTopBar(
            form,
            currentProjectTop,
            updateTop,
            button => settingsMenu.Show(button,
                new Point(button.Width - settingsMenu.PreferredSize.Width, button.Height)));
        FindButton(topBar, "Settings")!.ContextMenuStrip = settingsMenu;

        RestyleGlobalActions(form, globalActions);

        var statusRail = BuildStatusRail(
            form,
            healthRail,
            healthDetail,
            updateRail,
            projectRail,
            globalActions);

        shell.Controls.Add(navigation, 0, 0);
        shell.SetRowSpan(navigation, 2);
        shell.Controls.Add(topBar, 1, 0);
        shell.SetColumnSpan(topBar, 2);
        shell.Controls.Add(dashboardFlow, 1, 1);
        shell.Controls.Add(statusRail, 2, 1);

        form.Controls.Add(shell);
        shell.BringToFront();

        void UpdateChrome()
        {
            foreach (var card in adapterCards)
                UpdateAdapterSummary(card);

            var localValues = GetStatusValues(localCard);
            var blenderValues = GetStatusValues(blenderCard);
            var godotValues = GetStatusValues(godotCard);

            var projectText = godotValues.Count > 0
                ? StripStatusBullet(godotValues[0].Text)
                : "Not configured";
            currentProjectTop.Text = $"Profile: Local workstation  •  Project: {projectText}";
            projectRail.Text = projectText;
            toolTip.SetToolTip(currentProjectTop, projectText);
            toolTip.SetToolTip(projectRail, projectText);

            var critical = new List<Label>();
            critical.AddRange(localValues);
            critical.AddRange(blenderValues);
            if (godotValues.Count > 0) critical.Add(godotValues[0]);
            if (godotValues.Count > 1) critical.Add(godotValues[1]);
            if (godotValues.Count > 2) critical.Add(godotValues[2]);
            if (godotValues.Count > 4) critical.Add(godotValues[4]);
            if (godotValues.Count > 5) critical.Add(godotValues[5]);

            var badCount = critical.Count(IsBadState);
            var warnCount = critical.Count(IsWarnState);
            if (badCount > 0)
            {
                healthRail.Text = "Needs attention";
                healthRail.ForeColor = Bad;
                healthDetail.Text = $"{badCount} core status item{(badCount == 1 ? "" : "s")} failed";
            }
            else if (warnCount > 0)
            {
                healthRail.Text = "Degraded";
                healthRail.ForeColor = Warn;
                healthDetail.Text = $"{warnCount} core status item{(warnCount == 1 ? "" : "s")} pending";
            }
            else if (critical.Count > 0 && critical.All(label => label.Text.Contains("Checking", StringComparison.OrdinalIgnoreCase)))
            {
                healthRail.Text = "Checking services…";
                healthRail.ForeColor = TextPrimary;
                healthDetail.Text = "Waiting for adapter status";
            }
            else
            {
                healthRail.Text = "Core services healthy";
                healthRail.ForeColor = Good;
                healthDetail.Text = "Runtime-idle states remain informational";
            }

            var updateState = ReadUpdateState(log.Text);
            updateTop.Text = updateState;
            updateRail.Text = updateState;
            updateRail.ForeColor = updateState.Contains("available", StringComparison.OrdinalIgnoreCase)
                ? Warn
                : updateState.Contains("failed", StringComparison.OrdinalIgnoreCase)
                    ? Bad
                    : TextSecondary;
        }

        uiTimer.Tick += (_, _) => UpdateChrome();
        uiTimer.Start();
        form.FormClosed += (_, _) =>
        {
            uiTimer.Stop();
            uiTimer.Dispose();
            toolTip.Dispose();
        };

        form.Shown += (_, _) =>
        {
            ResizeDashboardChildren();
            UpdateChrome();
        };

        ResizeDashboardChildren();
        form.ResumeLayout(true);
    }

    private static Control BuildNavigation(
        MainForm form,
        Action onHome,
        Action onDiagnostics,
        Action onUpdates,
        Action onSettings)
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(10, 16, 21),
            Padding = new Padding(Scale(form, 14))
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 8,
            Margin = Padding.Empty
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, Scale(form, 86)));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, Scale(form, 24)));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, Scale(form, 48)));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, Scale(form, 48)));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, Scale(form, 48)));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, Scale(form, 48)));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, Scale(form, 32)));

        var brand = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = Padding.Empty
        };
        brand.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Scale(form, 62)));
        brand.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var icon = new PictureBox
        {
            Dock = DockStyle.Fill,
            SizeMode = PictureBoxSizeMode.Zoom,
            Margin = new Padding(0, 0, Scale(form, 8), 0),
            Image = LoadAsset("WatermelonCat.png")
        };
        brand.Controls.Add(icon, 0, 0);

        var brandText = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = Padding.Empty
        };
        brandText.RowStyles.Add(new RowStyle(SizeType.Percent, 55));
        brandText.RowStyles.Add(new RowStyle(SizeType.Percent, 45));
        brandText.Controls.Add(MakeTextLabel("Constantine", 12.5F, TextPrimary, bold: true), 0, 0);
        brandText.Controls.Add(MakeTextLabel("HUB", 9.5F, Accent, bold: true), 0, 1);
        brand.Controls.Add(brandText, 1, 0);
        layout.Controls.Add(brand, 0, 0);

        var section = MakeTextLabel("WORKSPACE", 8.5F, Color.FromArgb(113, 127, 140), bold: true);
        section.TextAlign = ContentAlignment.BottomLeft;
        layout.Controls.Add(section, 0, 1);

        var home = MakeNavButton(form, "Home", active: true, onHome);
        var diagnostics = MakeNavButton(form, "Diagnostics", active: false, onDiagnostics);
        var updates = MakeNavButton(form, "Updates", active: false, onUpdates);
        layout.Controls.Add(home, 0, 2);
        layout.Controls.Add(diagnostics, 0, 3);
        layout.Controls.Add(updates, 0, 4);

        var settings = MakeNavButton(form, "Settings", active: false, onSettings);
        layout.Controls.Add(settings, 0, 6);

        var footer = MakeTextLabel("Local control plane", 8.5F, Color.FromArgb(103, 116, 128));
        footer.TextAlign = ContentAlignment.BottomLeft;
        layout.Controls.Add(footer, 0, 7);

        panel.Controls.Add(layout);
        return panel;
    }

    private static Control BuildTopBar(
        MainForm form,
        Label currentProject,
        Label updateState,
        Action<Control> openSettings)
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Surface,
            Padding = new Padding(Scale(form, 18), Scale(form, 10), Scale(form, 16), Scale(form, 10))
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            Margin = Padding.Empty
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 32));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 18));

        var left = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = Padding.Empty
        };
        left.RowStyles.Add(new RowStyle(SizeType.Percent, 58));
        left.RowStyles.Add(new RowStyle(SizeType.Percent, 42));
        left.Controls.Add(MakeTextLabel("Dashboard", 15F, TextPrimary, bold: true), 0, 0);
        currentProject.Dock = DockStyle.Fill;
        left.Controls.Add(currentProject, 0, 1);
        layout.Controls.Add(left, 0, 0);

        var middle = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = Padding.Empty
        };
        middle.RowStyles.Add(new RowStyle(SizeType.Percent, 52));
        middle.RowStyles.Add(new RowStyle(SizeType.Percent, 48));
        var version = MakeTextLabel($"v{GetDisplayVersion()}", 10F, TextPrimary, bold: true);
        version.TextAlign = ContentAlignment.MiddleRight;
        updateState.Dock = DockStyle.Fill;
        middle.Controls.Add(version, 0, 0);
        middle.Controls.Add(updateState, 0, 1);
        layout.Controls.Add(middle, 1, 0);

        Button settingsButton = null!;
        settingsButton = MakeActionButton(form, "Settings", primary: false, () => openSettings(settingsButton));
        settingsButton.Dock = DockStyle.Right;
        settingsButton.Margin = new Padding(Scale(form, 12), Scale(form, 4), 0, Scale(form, 4));
        layout.Controls.Add(settingsButton, 2, 0);

        panel.Controls.Add(layout);
        return panel;
    }

    private static Control BuildWelcomeCard(MainForm form)
    {
        var card = new Panel
        {
            Height = Scale(form, 96),
            BackColor = SurfaceRaised,
            Padding = new Padding(Scale(form, 14)),
            Margin = new Padding(0, 0, 0, Scale(form, 10))
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = Padding.Empty
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Scale(form, 76)));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var mascot = new PictureBox
        {
            Dock = DockStyle.Fill,
            SizeMode = PictureBoxSizeMode.Zoom,
            Margin = new Padding(0, 0, Scale(form, 12), 0),
            Image = LoadAsset("WatermelonCatWaving.png")
        };
        layout.Controls.Add(mascot, 0, 0);

        var copy = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = Padding.Empty
        };
        copy.RowStyles.Add(new RowStyle(SizeType.Percent, 54));
        copy.RowStyles.Add(new RowStyle(SizeType.Percent, 46));
        copy.Controls.Add(MakeTextLabel("MCP control plane", 16F, TextPrimary, bold: true), 0, 0);
        copy.Controls.Add(MakeTextLabel(
            "Quick operational view. Expand a service only when you need its details or controls.",
            9.5F,
            TextSecondary), 0, 1);
        layout.Controls.Add(copy, 1, 0);
        card.Controls.Add(layout);
        return card;
    }

    private static AdapterCardHost BuildAdapterHost(
        MainForm form,
        Control originalCard,
        string title,
        string badgeText,
        Color badgeColor,
        int preferredSummaryIndex,
        ToolTip toolTip)
    {
        RestyleLegacyCard(form, originalCard);

        var oldHeight = Math.Max(originalCard.Height, Scale(form, 200));
        var host = new Panel
        {
            Height = Scale(form, 88),
            BackColor = SurfaceRaised,
            Padding = new Padding(1),
            Margin = new Padding(0, 0, 0, Scale(form, 10))
        };

        var inner = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = SurfaceRaised,
            Margin = Padding.Empty
        };
        inner.RowStyles.Add(new RowStyle(SizeType.Absolute, Scale(form, 84)));
        inner.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        host.Controls.Add(inner);

        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 5,
            RowCount = 1,
            Padding = new Padding(Scale(form, 12), Scale(form, 9), Scale(form, 10), Scale(form, 9)),
            Margin = Padding.Empty
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Scale(form, 48)));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Scale(form, 168)));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Scale(form, 42)));

        var badge = new Label
        {
            Text = badgeText,
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(
                Math.Max(0, badgeColor.R - 100),
                Math.Max(0, badgeColor.G - 75),
                Math.Max(0, badgeColor.B - 40)),
            ForeColor = badgeColor,
            Font = new Font("Segoe UI Semibold", 11F),
            TextAlign = ContentAlignment.MiddleCenter,
            Margin = new Padding(0, 0, Scale(form, 10), 0)
        };
        header.Controls.Add(badge, 0, 0);

        var titleLabel = MakeTextLabel(title, 12.5F, TextPrimary, bold: true);
        titleLabel.Dock = DockStyle.Fill;
        titleLabel.TextAlign = ContentAlignment.MiddleLeft;
        header.Controls.Add(titleLabel, 1, 0);

        var summary = MakeTextLabel("Checking…", 9.5F, TextSecondary);
        summary.Dock = DockStyle.Fill;
        summary.TextAlign = ContentAlignment.MiddleLeft;
        summary.AutoEllipsis = true;
        summary.Margin = new Padding(Scale(form, 6), 0, Scale(form, 8), 0);
        header.Controls.Add(summary, 2, 0);

        var start = MakeActionButton(form, "Start", primary: true,
            () => MainForm.InvokeButtonAction(FindButton(originalCard, "Start")));
        start.Margin = new Padding(Scale(form, 4), Scale(form, 3), Scale(form, 8), Scale(form, 3));
        header.Controls.Add(start, 3, 0);

        var toggle = new Button
        {
            Text = "⌄",
            Dock = DockStyle.Fill,
            FlatStyle = FlatStyle.Flat,
            BackColor = SurfaceHover,
            ForeColor = TextPrimary,
            Font = new Font("Segoe UI Semibold", 13F),
            Margin = new Padding(0, Scale(form, 3), 0, Scale(form, 3)),
            TabStop = false
        };
        toggle.FlatAppearance.BorderColor = Border;
        header.Controls.Add(toggle, 4, 0);
        inner.Controls.Add(header, 0, 0);

        var body = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = SurfaceRaised,
            Padding = new Padding(Scale(form, 10), 0, Scale(form, 10), Scale(form, 10)),
            Visible = false,
            Margin = Padding.Empty
        };
        originalCard.Dock = DockStyle.Fill;
        originalCard.Margin = Padding.Empty;
        body.Controls.Add(originalCard);
        inner.Controls.Add(body, 0, 1);

        var expanded = false;
        var expandedHeight = Scale(form, 86) + Math.Max(Scale(form, 170), oldHeight - Scale(form, 26));

        void Toggle()
        {
            expanded = !expanded;
            body.Visible = expanded;
            host.Height = expanded ? expandedHeight : Scale(form, 88);
            toggle.Text = expanded ? "⌃" : "⌄";
        }

        toggle.Click += (_, _) => Toggle();
        header.DoubleClick += (_, _) => Toggle();
        titleLabel.DoubleClick += (_, _) => Toggle();

        return new AdapterCardHost(host, originalCard, summary, preferredSummaryIndex, toolTip);
    }

    private static Control BuildLogPanel(
        MainForm form,
        RichTextBox log,
        out Action<bool> setExpanded)
    {
        var host = new Panel
        {
            Height = Scale(form, 218),
            BackColor = SurfaceRaised,
            Padding = new Padding(1),
            Margin = new Padding(0, 0, 0, Scale(form, 10))
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = Padding.Empty
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, Scale(form, 42)));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(Scale(form, 12), Scale(form, 4), Scale(form, 8), Scale(form, 4)),
            Margin = Padding.Empty
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Scale(form, 42)));
        var title = MakeTextLabel("Diagnostics log", 10.5F, TextPrimary, bold: true);
        title.Dock = DockStyle.Fill;
        header.Controls.Add(title, 0, 0);

        var toggle = new Button
        {
            Text = "⌃",
            Dock = DockStyle.Fill,
            FlatStyle = FlatStyle.Flat,
            BackColor = SurfaceHover,
            ForeColor = TextPrimary,
            Margin = new Padding(0),
            TabStop = false
        };
        toggle.FlatAppearance.BorderColor = Border;
        header.Controls.Add(toggle, 1, 0);

        log.Dock = DockStyle.Fill;
        log.BorderStyle = BorderStyle.None;
        log.BackColor = Color.FromArgb(8, 13, 18);
        log.ForeColor = Color.FromArgb(213, 219, 224);
        log.Font = new Font("Cascadia Mono", 9.0F);
        log.Margin = Padding.Empty;

        layout.Controls.Add(header, 0, 0);
        layout.Controls.Add(log, 0, 1);
        host.Controls.Add(layout);

        var expanded = true;
        Action<bool> applyExpanded = value =>
        {
            expanded = value;
            layout.RowStyles[1].SizeType = value ? SizeType.Percent : SizeType.Absolute;
            layout.RowStyles[1].Height = value ? 100 : 0;
            log.Visible = value;
            host.Height = value ? Scale(form, 218) : Scale(form, 44);
            toggle.Text = value ? "⌃" : "⌄";
        };
        setExpanded = applyExpanded;

        toggle.Click += (_, _) => applyExpanded(!expanded);
        header.DoubleClick += (_, _) => applyExpanded(!expanded);
        return host;
    }

    private static Control BuildStatusRail(
        MainForm form,
        Label health,
        Label healthDetail,
        Label update,
        Label project,
        FlowLayoutPanel globalActions)
    {
        var rail = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(10, 16, 21),
            Padding = new Padding(Scale(form, 12))
        };

        var flow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            BackColor = Color.Transparent,
            Padding = Padding.Empty,
            Margin = Padding.Empty
        };
        rail.Controls.Add(flow);

        flow.Controls.Add(BuildRailCard(form, "GLOBAL HEALTH", health, healthDetail));
        flow.Controls.Add(BuildRailCard(form, "UPDATE STATUS", update, MakeTextLabel("Stable channel", 8.8F, TextSecondary)));
        flow.Controls.Add(BuildRailCard(form, "CURRENT PROJECT", project, MakeTextLabel("Profile: Local workstation", 8.8F, TextSecondary)));

        var safety = new Panel
        {
            Height = Scale(form, 92),
            BackColor = Surface,
            Padding = new Padding(Scale(form, 12)),
            Margin = new Padding(0, 0, 0, Scale(form, 10))
        };
        var safetyLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        safetyLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 45));
        safetyLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 55));
        safetyLayout.Controls.Add(MakeTextLabel("OWNERSHIP SAFETY", 8.7F, Accent, bold: true), 0, 0);
        safetyLayout.Controls.Add(MakeTextLabel("External / adopted processes are protected.", 9.0F, TextSecondary), 0, 1);
        safety.Controls.Add(safetyLayout);
        flow.Controls.Add(safety);

        var actionsTitle = MakeTextLabel("QUICK ACTIONS", 8.7F, Color.FromArgb(113, 127, 140), bold: true);
        actionsTitle.Height = Scale(form, 28);
        actionsTitle.Margin = new Padding(0, Scale(form, 4), 0, Scale(form, 4));
        flow.Controls.Add(actionsTitle);

        globalActions.FlowDirection = FlowDirection.TopDown;
        globalActions.WrapContents = false;
        globalActions.AutoScroll = false;
        globalActions.AutoSize = true;
        globalActions.BackColor = Color.Transparent;
        globalActions.Padding = Padding.Empty;
        globalActions.Margin = Padding.Empty;

        foreach (var button in globalActions.Controls.OfType<Button>())
        {
            button.Width = Scale(form, 226);
            button.Height = Scale(form, 38);
            button.Margin = new Padding(0, 0, 0, Scale(form, 6));
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderColor = Border;
            button.ForeColor = TextPrimary;
            button.BackColor = button.Text.Equals("Start All", StringComparison.OrdinalIgnoreCase)
                ? Accent
                : SurfaceHover;
        }

        flow.Controls.Add(globalActions);

        void ResizeRailCards()
        {
            var width = Math.Max(Scale(form, 210), flow.ClientSize.Width - flow.Padding.Horizontal - Scale(form, 2));
            foreach (Control child in flow.Controls)
                child.Width = width;
        }

        flow.SizeChanged += (_, _) => ResizeRailCards();
        ResizeRailCards();
        return rail;
    }

    private static Control BuildRailCard(MainForm form, string title, Label primary, Label secondary)
    {
        var card = new Panel
        {
            Height = Scale(form, 102),
            BackColor = Surface,
            Padding = new Padding(Scale(form, 12)),
            Margin = new Padding(0, 0, 0, Scale(form, 10))
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Margin = Padding.Empty
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, Scale(form, 22)));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 48));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 52));

        var caption = MakeTextLabel(title, 8.7F, Accent, bold: true);
        caption.Dock = DockStyle.Fill;
        primary.Dock = DockStyle.Fill;
        secondary.Dock = DockStyle.Fill;
        primary.AutoEllipsis = true;
        secondary.AutoEllipsis = true;

        layout.Controls.Add(caption, 0, 0);
        layout.Controls.Add(primary, 0, 1);
        layout.Controls.Add(secondary, 0, 2);
        card.Controls.Add(layout);
        return card;
    }

    private static void RestyleLegacyCard(MainForm form, Control card)
    {
        card.BackColor = SurfaceRaised;
        card.Padding = new Padding(0);
        card.Margin = Padding.Empty;

        var layout = card.Controls.OfType<TableLayoutPanel>().FirstOrDefault();
        if (layout is not null)
        {
            var title = layout.GetControlFromPosition(0, 0);
            if (title is not null)
            {
                title.Visible = false;
                layout.RowStyles[0].SizeType = SizeType.Absolute;
                layout.RowStyles[0].Height = 0;
            }

            var content = layout.GetControlFromPosition(0, 1) as TableLayoutPanel;
            if (content is not null)
            {
                content.Padding = new Padding(Scale(form, 2), Scale(form, 4), Scale(form, 2), 0);
                content.ColumnStyles.Clear();
                content.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

                foreach (Control child in content.Controls)
                {
                    child.Font = new Font("Segoe UI", 10.0F);
                    child.Margin = new Padding(Scale(form, 2), Scale(form, 2), Scale(form, 8), Scale(form, 2));
                }
            }

            var actions = layout.GetControlFromPosition(0, 2) as FlowLayoutPanel;
            if (actions is not null)
            {
                actions.AutoScroll = false;
                actions.WrapContents = true;
                actions.Padding = new Padding(Scale(form, 2), Scale(form, 6), Scale(form, 2), 0);
                foreach (var button in actions.Controls.OfType<Button>())
                {
                    button.AutoSize = true;
                    button.AutoSizeMode = AutoSizeMode.GrowAndShrink;
                    button.MinimumSize = new Size(Scale(form, 92), Scale(form, 36));
                    button.Height = Scale(form, 36);
                    button.Padding = new Padding(Scale(form, 10), 0, Scale(form, 10), 0);
                    button.Margin = new Padding(0, 0, Scale(form, 7), Scale(form, 6));
                    button.FlatStyle = FlatStyle.Flat;
                    button.FlatAppearance.BorderColor = Border;
                    button.ForeColor = TextPrimary;
                    button.BackColor = button.Text.Equals("Start", StringComparison.OrdinalIgnoreCase)
                        ? Accent
                        : SurfaceHover;
                }
            }
        }
    }

    private static void RestyleGlobalActions(MainForm form, FlowLayoutPanel actions)
    {
        actions.Font = new Font("Segoe UI", 9.5F);
        foreach (var button in actions.Controls.OfType<Button>())
        {
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderColor = Border;
            button.ForeColor = TextPrimary;
            button.BackColor = button.Text.Equals("Start All", StringComparison.OrdinalIgnoreCase)
                ? Accent
                : SurfaceHover;
        }
    }

    private static void UpdateAdapterSummary(AdapterCardHost host)
    {
        var values = GetStatusValues(host.OriginalCard);
        if (values.Count == 0)
        {
            host.Summary.Text = "Status unavailable";
            host.Summary.ForeColor = TextSecondary;
            return;
        }

        var index = Math.Clamp(host.PreferredSummaryIndex, 0, values.Count - 1);
        var selected = values[index];
        var text = StripStatusBullet(selected.Text);
        if (host.OriginalCard is not null &&
            values.Count > 1 &&
            text.Contains("Checking", StringComparison.OrdinalIgnoreCase))
        {
            text = StripStatusBullet(values[0].Text);
            selected = values[0];
        }

        host.Summary.Text = text;
        host.Summary.ForeColor = IsBadState(selected)
            ? Bad
            : IsWarnState(selected)
                ? Warn
                : selected.ForeColor;
        host.ToolTip.SetToolTip(host.Summary, text);
    }

    private static List<Label> GetStatusValues(Control card)
    {
        var layout = card.Controls.OfType<TableLayoutPanel>().FirstOrDefault();
        var content = layout?.GetControlFromPosition(0, 1) as TableLayoutPanel;
        if (content is null)
            return new List<Label>();

        var result = new List<Label>();
        for (var row = 0; row < content.RowCount; row++)
        {
            if (content.GetControlFromPosition(1, row) is Label value)
                result.Add(value);
        }
        return result;
    }

    private static Button? FindButton(Control root, string text)
    {
        foreach (Control child in root.Controls)
        {
            if (child is Button button &&
                button.Text.Equals(text, StringComparison.OrdinalIgnoreCase))
                return button;

            var nested = FindButton(child, text);
            if (nested is not null)
                return nested;
        }
        return null;
    }

    private static ContextMenuStrip BuildSettingsMenu(
        Control owner,
        Button? localSettings,
        Button? update,
        Button? saveLog)
    {
        var menu = new ContextMenuStrip
        {
            BackColor = SurfaceRaised,
            ForeColor = TextPrimary,
            ShowImageMargin = false
        };
        void InvokeAfterClose(Button? button)
        {
            if (!owner.IsDisposed && owner.IsHandleCreated)
                owner.BeginInvoke(new Action(() => MainForm.InvokeButtonAction(button)));
        }
        menu.Items.Add("Local Files allowlist", null, (_, _) => InvokeAfterClose(localSettings));
        menu.Items.Add("Check for updates", null, (_, _) => InvokeAfterClose(update));
        menu.Items.Add("Save diagnostic log", null, (_, _) => InvokeAfterClose(saveLog));
        // WinForms still accesses the dropdown after Closed; keep it alive with its owner.
        owner.Disposed += (_, _) => menu.Dispose();
        return menu;
    }

    private static Button MakeNavButton(MainForm form, string text, bool active, Action click)
    {
        var button = new Button
        {
            Text = text,
            Dock = DockStyle.Fill,
            FlatStyle = FlatStyle.Flat,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(Scale(form, 12), 0, Scale(form, 8), 0),
            Margin = new Padding(0, Scale(form, 3), 0, Scale(form, 3)),
            BackColor = active ? Color.FromArgb(38, 44, 49) : Color.FromArgb(10, 16, 21),
            ForeColor = active ? TextPrimary : TextSecondary,
            Font = new Font("Segoe UI Semibold", 10F),
            Cursor = Cursors.Hand
        };
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseOverBackColor = SurfaceHover;
        button.Click += (_, _) => click();
        return button;
    }

    private static Button MakeActionButton(MainForm form, string text, bool primary, Action click)
    {
        var button = new Button
        {
            Text = text,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(Scale(form, 86), Scale(form, 34)),
            Padding = new Padding(Scale(form, 10), 0, Scale(form, 10), 0),
            FlatStyle = FlatStyle.Flat,
            BackColor = primary ? Accent : SurfaceHover,
            ForeColor = primary ? Color.FromArgb(22, 24, 26) : TextPrimary,
            Font = new Font("Segoe UI Semibold", 9.5F),
            Cursor = Cursors.Hand
        };
        button.FlatAppearance.BorderColor = primary ? Accent : Border;
        button.FlatAppearance.MouseOverBackColor = primary ? AccentHover : Color.FromArgb(41, 54, 67);
        button.Click += (_, _) => click();
        return button;
    }

    private static Label MakeTextLabel(string text, float size, Color color, bool bold = false)
    {
        return new Label
        {
            Text = text,
            Dock = DockStyle.Fill,
            ForeColor = color,
            Font = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular),
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
            Margin = Padding.Empty
        };
    }

    private static Image? LoadAsset(string fileName)
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, fileName),
            Path.Combine(AppContext.BaseDirectory, "Assets", fileName)
        };

        foreach (var path in candidates)
        {
            try
            {
                if (!File.Exists(path))
                    continue;

                using var source = Image.FromFile(path);
                return new Bitmap(source);
            }
            catch
            {
                // Optional UI art must never block Hub startup.
            }
        }
        return null;
    }

    private static string ReadUpdateState(string logText)
    {
        if (logText.Contains("Update FAILED:", StringComparison.OrdinalIgnoreCase))
            return "Stable • update check failed";
        if (logText.Contains("Update available:", StringComparison.OrdinalIgnoreCase))
            return "Stable • update available";
        if (logText.Contains("No newer stable release is available.", StringComparison.OrdinalIgnoreCase))
            return "Stable • up to date";
        if (logText.Contains("Checking GitHub Releases", StringComparison.OrdinalIgnoreCase))
            return "Stable • checking…";
        return "Stable • auto-check enabled";
    }

    private static bool IsBadState(Label label)
        => label.ForeColor.R >= 210 && label.ForeColor.G < 150;

    private static bool IsWarnState(Label label)
        => label.ForeColor.R >= 210 && label.ForeColor.G >= 150 && label.ForeColor.B < 130;

    private static string StripStatusBullet(string value)
    {
        var text = value.Trim();
        if (text.StartsWith("●", StringComparison.Ordinal))
            text = text[1..].TrimStart();
        return text;
    }

    private static string GetDisplayVersion()
    {
        var version = typeof(MainForm).Assembly.GetName().Version;
        return version is null ? "0.1.0" : $"{version.Major}.{version.Minor}.{version.Build}";
    }

    private static int Scale(Control control, int logical)
        => Math.Max(1, (int)Math.Round(logical * Math.Max(96, control.DeviceDpi) / 96.0));

    private sealed record AdapterCardHost(
        Panel Host,
        Control OriginalCard,
        Label Summary,
        int PreferredSummaryIndex,
        ToolTip ToolTip);
}
