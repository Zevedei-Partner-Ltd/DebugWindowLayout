using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using System;
using System.ComponentModel.Design;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace DebugWindowLayout
{
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [InstalledProductRegistration("Debug Window Layout", "Arranges debug process windows across monitors.", "1.0")]
    [ProvideMenuResource("Menus.ctmenu", 1)]
    [ProvideOptionPage(typeof(DebugWindowLayoutOptionsPage), "Debug Window Layout", "General", 0, 0, true)]
    [ProvideAutoLoad(VSConstants.UICONTEXT.SolutionExists_string, PackageAutoLoadFlags.BackgroundLoad)]
    [Guid(PackageGuidString)]
    public sealed class DebugWindowLayoutPackage : AsyncPackage
    {
        public const string PackageGuidString = "5B706A35-5BB7-4B56-B3B0-07C7B9FBC61E";
        private static readonly Guid CommandSet = new Guid("C2D36C87-3563-4F88-BEC1-8F4154B79F47");
        private const int ArrangeNowCommandId = 0x0100;
        private const int OpenConfigCommandId = 0x0101;
        private const int OpenOptionsCommandId = 0x0102;
        private const int CaptureWindowsCommandId = 0x0103;

        private DTE2 _dte;
        private DebuggerEvents _debuggerEvents;
        private DebugWindowLayoutController _controller;
        private bool _debugSessionActive;

        protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
        {
            await base.InitializeAsync(cancellationToken, progress);
            await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

            _dte = await GetServiceAsync(typeof(SDTE)) as DTE2;
            if (_dte == null)
                return;

            _controller = new DebugWindowLayoutController(this, _dte);

            var commandService = await GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
            if (commandService != null)
            {
                commandService.AddCommand(new MenuCommand(
                    (s, e) => _controller.ArrangeNow(),
                    new CommandID(CommandSet, ArrangeNowCommandId)));

                commandService.AddCommand(new MenuCommand(
                    (s, e) => _controller.CaptureWindows(),
                    new CommandID(CommandSet, CaptureWindowsCommandId)));

                commandService.AddCommand(new MenuCommand(
                    (s, e) => _controller.OpenConfig(),
                    new CommandID(CommandSet, OpenConfigCommandId)));

                commandService.AddCommand(new MenuCommand(
                    (s, e) => ShowOptionPage(typeof(DebugWindowLayoutOptionsPage)),
                    new CommandID(CommandSet, OpenOptionsCommandId)));
            }

            _debuggerEvents = _dte.Events.DebuggerEvents;
            _debuggerEvents.OnEnterRunMode += OnEnterRunMode;
            _debuggerEvents.OnEnterDesignMode += OnEnterDesignMode;

            // If the package was loaded after debugging already started, still arrange this session.
            if (_dte.Debugger.CurrentMode != dbgDebugMode.dbgDesignMode)
            {
                _debugSessionActive = true;
                _controller.ScheduleAutoArrange();
            }
        }

        private void OnEnterRunMode(dbgEventReason reason)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_debugSessionActive)
                return;

            _debugSessionActive = true;
            _controller?.ScheduleAutoArrange();
        }

        private void OnEnterDesignMode(dbgEventReason reason)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            _debugSessionActive = false;
        }

    }
}
