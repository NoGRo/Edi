using Edi.Core;
using Edi.Forms;
using Microsoft.Extensions.DependencyInjection.Extensions;

Thread thread = new Thread(() =>
{
    var appForm = new App();
    appForm.Run();
});
thread.SetApartmentState(ApartmentState.STA);
thread.Start();


