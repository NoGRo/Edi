﻿# Edi.Core - Easy Device Integrator Core

Edi.Core is a .NET library that simplifies device management, providing developers with an efficient and intuitive way to control and interact with various devices. 

## Key Features

- Simple, intuitive device management through the Gallery system
- Easy control over tasks: pause, resume, stop
- Configuration and management of different types of tasks
- Ideal for game development and applications that require flexible device control

## Getting Started

Using Edi.Core is straightforward. Here's how to get started:

First, make sure you have the Edi.Core package installed. If not, you can get it from NuGet:

```shell
Install-Package Edi.Core
```

Next, add Edi.Core to your services:

```csharp
using Edi.Core;
...
builder.Services.AddEdi();
```

After setting up the services, you can get an instance of `IEdi`:

```csharp
var edi = serviceScope.ServiceProvider.GetRequiredService<IEdi>();
await edi.Init();
```

Alternatively, you can also create an instance of `Edi` using the `EdiBuilder`:

```csharp
var edi = EdiBuilder.Create("appsetting.json");
await edi.Init();
```

And there you have it! You're now ready to start using Edi.Core in your application.

## Contact

If you have any issues, questions, or suggestions, feel free to open an issue on this repository. Contributions are also welcome!
