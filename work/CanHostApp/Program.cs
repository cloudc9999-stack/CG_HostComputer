namespace CanHost;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        AppTheme.Load();
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.ThreadException += (_, e) => ShowFatalError(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
            {
                ShowFatalError(ex);
            }
        };

        Application.Run(new MainForm());
    }

    private static void ShowFatalError(Exception exception)
    {
        MessageBox.Show(
            $"程序发生未处理错误：\n\n{exception.Message}",
            "CAN 上位机",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
    }
}
