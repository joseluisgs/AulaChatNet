# AulaChatNet 💬

[![.NET](https://img.shields.io/badge/.NET-10-blue)](https://dotnet.microsoft.com/)
[![ASP.NET Core](https://img.shields.io/badge/ASP.NET%20Core-10-blue)](https://dotnet.microsoft.com/en-us/apps/aspnet)
[![C#](https://img.shields.io/badge/C%23-14-blue)](https://docs.microsoft.com/en-us/dotnet/csharp/)
[![SignalR](https://img.shields.io/badge/SignalR-Tiempo%20real-purple)](https://learn.microsoft.com/aspnet/core/signalr/introduction)
[![Rx.NET](https://img.shields.io/badge/Rx.NET-Reactivo-orange)](https://github.com/dotnet/reactive)


**Chat en tiempo real para el aula con .NET 10, SignalR y Rx.NET. Clientes para terminal y web (Blazor), con estética de Discord.**

> **README inicial.** Este documento recoge la idea y la arquitectura de referencia del proyecto. La documentación completa se irá añadiendo por fases, como en [TiendaDawApi-NetCore](https://github.com/joseluisgs/TiendaDawApi-NetCore).

## 🎯 Descripción

AulaChatNet es un proyecto educativo de ejemplo para el módulo **Desarrollo Web en Servidor** (ciclos 2DAW y DAM). El objetivo docente es que el alumnado entienda cómo **SignalR** (transporte en tiempo real) y **Rx.NET** (composición de flujos) se complementan en una arquitectura .NET moderna, y cómo diseñar una solución con **múltiples clientes** sobre el mismo hub.

- 📡 **SignalR**: un único `ChatHub` en el servidor; mensajes, entradas y salidas en tiempo real
- 🔄 **Rx.NET**: los mensajes entrantes se exponen como `IObservable<MensajeDto>`; la UI se suscribe aplicando operadores reactivos (filtrar, agrupar, formatear)
- 💻 **Múltiples clientes**: consola con Spectre.Console y web con Blazor WebAssembly, ambos sobre el mismo hub
- 🎨 **Estética Discord**: layout de dos columnas, avatares (DiceBear) y paleta oscura
- 🗄️ **Fase 1 en memoria**: estado en `ConcurrentDictionary`, sin base de datos

## 🚀 Tecnologías

- **.NET 10 con C# 14** - Plataforma principal
- **ASP.NET Core + SignalR** - Servidor en tiempo real
- **Rx.NET (System.Reactive)** - Programación reactiva en los clientes
- **Blazor WebAssembly** - Cliente web
- **Spectre.Console** - Cliente de terminal
- **DiceBear API** - Avatares deterministas por nombre de usuario
- **NUnit + FluentAssertions** - Tests por proyecto

## 📂 Estructura del Proyecto

```
AulaChatNet/
├── AulaChatNet.Shared/             # DTOs, constantes del hub, avatar, operadores Rx
├── AulaChatNet.Server/             # ASP.NET Core + SignalR (ChatHub)
├── AulaChatNet.Client.Terminal/    # Consola con Spectre.Console
├── AulaChatNet.Client.Blazor/      # Blazor WebAssembly
└── Xxx.Tests/                      # Tests hermanos de cada proyecto
```

> **En construcción.** Fase 1 (MVP en memoria) en desarrollo. Cada fase ampliará este README con arquitectura, endpoints y capturas.

## 📝 Licencia

Este proyecto es un ejemplo educativo con fines didácticos.

## 👨‍💻 Autor

Codificado con :sparkling_heart: por [José Luis González Sánchez](https://joseluisgs.dev)

[![GitHub](https://img.shields.io/github/followers/joseluisgs?style=social)](https://github.com/joseluisgs)
[![GitHub](https://img.shields.io/github/stars/joseluisgs?style=social)](https://github.com/joseluisgs)

### Contacto

<p>
    <a href="https://joseluisgs.dev/" target="_blank">
        <img loading="lazy" src="https://raw.githubusercontent.com/joseluisgs/joseluisgs/master/images/social-icons/favicon.png" height="32">
    </a>&nbsp;
    <a href="https://github.com/joseluisgs" target="_blank">
        <img loading="lazy" src="https://raw.githubusercontent.com/joseluisgs/joseluisgs/master/images/social-icons/github.svg" height="32">
    </a>&nbsp;
    <a href="https://www.linkedin.com/in/JoseLuisGSDev" target="_blank">
        <img loading="lazy" src="https://raw.githubusercontent.com/joseluisgs/joseluisgs/master/images/social-icons/linkedin.png" height="32">
    </a>&nbsp;
    <a href="https://www.youtube.com/@joseluisgs" target="_blank">
        <img loading="lazy" src="https://raw.githubusercontent.com/joseluisgs/joseluisgs/master/images/social-icons/youtube.png" height="32">
    </a>&nbsp;
    <a href="https://x.com/JoseLuisGSDev" target="_blank">
        <img loading="lazy" src="https://raw.githubusercontent.com/joseluisgs/joseluisgs/master/images/social-icons/twitter.png" height="32">
    </a>&nbsp;
    <a href="https://www.instagram.com/joseluisgs.dev/" target="_blank">
        <img loading="lazy" src="https://raw.githubusercontent.com/joseluisgs/joseluisgs/master/images/social-icons/instagram.png" height="32">
    </a>
</p>

## Licencia de uso

Este repositorio está licenciado bajo **Creative Commons**. Por favor si compartes, usas o modifica este proyecto cita a su autor.

<a rel="license" href="https://creativecommons.org/licenses/by-nc-sa/4.0/deed.es"><img alt="Licencia de Creative Commons" style="border-width:0" src="https://i.creativecommons.org/l/by-nc-sa/4.0/88x31.png" /></a>
