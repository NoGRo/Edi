using Edi.Core;
using Edi.Forms;
using Microsoft.Extensions.DependencyInjection.Extensions;

Thread thread = new Thread(() =>
{
    bool createdNew;
    using (Mutex mutex = new Mutex(true, "Edi", out createdNew))
    {
        if (!createdNew) {
            Environment.Exit(0);
            return;
        }
    }
    var appForm = new App();
    appForm.Run();
});
thread.SetApartmentState(ApartmentState.STA);
thread.Start();


