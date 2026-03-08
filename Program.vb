Imports System.Windows.Forms


    Public Module Program
        <STAThread()>
        Public Sub Main(args As String())
            Application.EnableVisualStyles()
            Application.SetCompatibleTextRenderingDefault(False)
            Application.Run(New MainForm(args))
        End Sub
    End Module
