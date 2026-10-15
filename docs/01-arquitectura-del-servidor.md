- [1. La arquitectura del servidor](#1-la-arquitectura-del-servidor)
  - [1.1. Los proyectos y su misión](#11-los-proyectos-y-su-misión)
  - [1.2. El viaje de un mensaje](#12-el-viaje-de-un-mensaje)
  - [1.3. Un patrón de errores, un solo formato](#13-un-patrón-de-errores-un-solo-formato)
- [2. El ciclo de vida de una conexión](#2-el-ciclo-de-vida-de-una-conexión)
  - [2.1. La secuencia completa](#21-la-secuencia-completa)
  - [2.2. Alta: ConnectAsync](#22-alta-connectasync)
  - [2.3. Envío: SendAsync](#23-envío-sendasync)
  - [2.4. Cambio de canal: CambiarCanalAsync](#24-cambio-de-canal-cambiarcanalasync)
  - [2.5. Desconexión: OnDisconnectedAsync](#25-desconexión-ondisconnectedasync)
  - [2.6. Cómo se prueba todo esto sin abrir una terminal](#26-cómo-se-prueba-todo-esto-sin-abrir-una-terminal)
- [3. El estado en memoria: ChatStore](#3-el-estado-en-memoria-chatstore)
  - [3.1. Dos estructuras, dos misiones](#31-dos-estructuras-dos-misiones)
  - [3.2. Unicidad de nombres sin bloqueos](#32-unicidad-de-nombres-sin-bloqueos)
  - [3.3. La cola circular del historial](#33-la-cola-circular-del-historial)
- [4. Los canales como grupos de SignalR](#4-los-canales-como-grupos-de-signalr)
  - [4.1. Un canal, un grupo](#41-un-canal-un-grupo)
  - [4.2. Configuración sin hardcodeos](#42-configuración-sin-hardcodeos)
  - [4.3. Normalización: daw, #daw y #DAW](#43-normalización-daw-daw-y-daw)
- [5. Logging con Serilog desde appsettings](#5-logging-con-serilog-desde-appsettings)
  - [5.1. Consola para ver, fichero para investigar](#51-consola-para-ver-fichero-para-investigar)
  - [5.2. El símbolo doble dos puntos de Serilog](#52-el-símbolo-doble-dos-puntos-de-serilog)
  - [5.3. Rotación de 7 días](#53-rotación-de-7-días)
- [6. Dos bugs reales del servidor](#6-dos-bugs-reales-del-servidor)
  - [6.1. La carrera de los diez Anas](#61-la-carrera-de-los-diez-anas)
  - [6.2. El ConfigurationBinder que duplica listas](#62-el-configurationbinder-que-duplica-listas)
- [7. Resumen del punto](#7-resumen-del-punto)



# 1. La arquitectura del servidor

> 💡 **Punto de partida:** abres Discord y entras al canal #general. En pantalla ves a 40 personas y el mensaje que escribió Marta hace tres minutos. Abres otra pestaña en #daw y ves a otras 12 y su historial, sin mezclarse. Nadie ha recargado nada: todo llega solo. ¿Cómo sabe el servidor quién está en cada canal? ¿Cómo hace para mandarte el historial correcto y no el de otro? ¿Y qué pasa si dos personas se ponen el mismo nombre a la vez?

En este punto diseñamos por dentro el servidor de AulaChatNet: la arquitectura por capas, el ciclo de vida completo de una conexión de SignalR, las estructuras de datos que sostienen los canales en memoria, por qué cada canal es un "grupo", cómo se configura el logging sin tocar código y, para rematar, los dos bugs reales que enseñó este servidor mientras se construía.

**Objetivos de aprendizaje:**

- Entender la arquitectura por capas Hub → Store → Shared y qué responsabilidad tiene cada una
- Seguir el ciclo de vida de una conexión SignalR: alta, envío, cambio de canal y desconexión
- Comprender por qué el estado vive en `ConcurrentDictionary` y qué es la concurrencia
- Ver la cola circular del historial y el límite configurable
- Usar los grupos de SignalR para que cada canal reciba solo lo suyo
- Configurar Serilog entero desde `appsettings.json`, con rotación de 7 días
- Conocer dos fallos reales: las carreras de concurrencia y el "truco" del ConfigurationBinder

## 1.1. Los proyectos y su misión

AulaChatNet es una solución con varios proyectos porque **el contrato entre servidor y clientes merece su propio proyecto**. Tanto el terminal como la web tienen que hablar el mismo idioma, y ese idioma (`Shared`) lo define nadie mejor que una capa común:

```mermaid
flowchart TD
    subgraph Clientes["Clientes (Fase 1)"]
        T["AulaChatNet.Client.Terminal<br/>consola elegante (Spectre.Console)"]
        B["AulaChatNet.Client.Blazor<br/>web (Blazor WebAssembly)"]
    end

    S["AulaChatNet.Server<br/>ASP.NET Core + SignalR<br/>ChatHub + ChatStore"]

    SH["AulaChatNet.Shared<br/>DTOs · constantes · ChatConfig<br/>avatar · operadores Rx"]

    T <-->|"SignalR"| S
    B <-->|"SignalR"| S
    T -.-> SH
    B -.-> SH
    S -.-> SH

    style T fill:#FF9800,color:#fff
    style B fill:#FF9800,color:#fff
    style S fill:#2196F3,color:#fff
    style SH fill:#9C27B0,color:#fff
```

Cada carpeta del servidor tiene una responsabilidad, y los nombres lo dicen todo:

| Carpeta | Contenido | Ejemplo |
|---------|-----------|---------|
| `Hubs/` | La puerta de entrada de SignalR | `ChatHub.cs` |
| `Services/` | La lógica y el estado | `IChatStore`, `ChatStore` |
| `Models/` | Los datos que usa el servidor | `EstadoCanal`, `ConexionChat` |
| `Infrastructures/` | Extensiones de arranque (registran servicios, mapean rutas) | `AddChatServices`, `MapChatHub` |

> 📝 **Nota:** el patrón de "extensiones en `Infrastructures/`" no es un capricho: es el mismo estilo que usan proyectos reales para dejar el `Program.cs` limpio y leerlo como un índice.

## 1.2. El viaje de un mensaje

Un mensaje escrito en el terminal viaja así hasta la pantalla de un compañero:

```mermaid
flowchart LR
    C["Usuario escribe<br/>'Buenas a todos'"] --> TC["Terminal<br/>ValidarMensaje"]
    TC -->|"InvokeAsync(SendAsync)"| H["ChatHub.SendAsync"]
    H --> CF["ChatConfig<br/>¿supera 500 caracteres?"]
    CF -->|"No"| ST["ChatStore.AddMensaje<br/>a #general"]
    ST --> GR["Clients.Group('#general')<br/>difunde MensajeNuevo"]
    GR --> PB["Blazor recibe el evento<br/>Rx lo procesa y pinta"]
    CF -->|"Sí"| E["Devuelve error<br/>string? con el mensaje"]

    style C fill:#4CAF50,color:#fff
    style TC fill:#FF9800,color:#fff
    style H fill:#2196F3,color:#fff
    style CF fill:#607D8B,color:#fff
    style ST fill:#9C27B0,color:#fff
    style GR fill:#4CAF50,color:#fff
    style PB fill:#FF9800,color:#fff
    style E fill:#f44336,color:#fff
```

📌 **Ejemplo real:** WhatsApp. Cuando envías un mensaje, tu móvil no espera a que el del otro esté encendido: el servidor lo recibe, lo guarda y lo reenvía a los que están conectados. Si el destinatario está fuera, el mensaje le llegará cuando se conecte (eso lo veremos en la Fase 2 con persistencia; de momento solo se envía a los conectados).

## 1.3. Un patrón de errores, un solo formato

SignalR devuelve las llamadas como `Task<T>`. En AulaChatNet, **los métodos del hub devuelven `Task<string?>`, y `null` siempre significa "todo correcto"**. Si hay un problema, devuelven el mensaje de error en español para que el cliente lo muestre.

```csharp
var error = await conexion.InvokeAsync<string?>(ChatHubMetodos.Enviar, texto);

if (error is not null)
{
    // Algo fue mal: el servidor nos explica qué
    Log.Warning(error);
    return;
}
```

El mismo patrón se repite en las validaciones de `ChatConfig`: los métodos `ValidarUsuario` y `ValidarMensaje` devuelven `string?` (null = OK), y en el store (`AddUsuario`, `CambiarCanal`...). **Una sola convención en todo el sistema**: si devuelves texto, hay un problema; si devuelves `null`, sigue adelante.

> 💡 **Consejo:** fíjate en que `ConnectAsync` y `CambiarCanalAsync` **también** devuelven `string?`. La diferencia es semántica: `ConnectAsync` valida el nombre Y lo registra; `CambiarCanalAsync` comprueba que estés registrado antes de moverte.

## 2. El ciclo de vida de una conexión

### 2.1. La secuencia completa

Este es el viaje completo de una conexión, desde que el cliente abre el WebSocket hasta que se desconecta. Presta atención a quién recibe cada cosa (el historial SOLO al que entra, los mensajes a todo el grupo):

```mermaid
sequenceDiagram
    autonumber
    participant C as Cliente
    participant H as ChatHub
    participant S as ChatStore

    C->>H: negotiate + WebSocket
    H->>H: OnConnectedAsync (aún sin nombre)

    C->>H: ConnectAsync("Ana", "#daw")
    H->>H: ChatConfig.EncontrarCanal("#daw")
    H->>S: AddUsuario("#daw", "Ana", conn-1)
    S-->>H: null (OK)
    H-->>C: HistorialRecibido (SOLO a Ana)
    H->>S: AddMensaje("#daw", "Ana se ha unido")
    H-->>C: MensajeNuevo (al grupo #daw)
    H-->>C: ListaUsuariosActualizada (al grupo #daw)

    C->>H: SendAsync("Buenas")
    H->>S: AddMensaje("#daw", mensaje)
    H-->>C: MensajeNuevo (al grupo #daw)

    Note over C,H: Ana escribe /canal #general

    C->>H: CambiarCanalAsync("#general")
    H->>S: CambiarCanal(conn-1, "#general")
    S-->>H: null (OK)
    H->>S: AddMensaje("#daw", "Ana ha salido")
    H-->>C: MensajeNuevo (al grupo #daw)
    H-->>C: ListaUsuariosActualizada (#daw)
    H-->>C: HistorialRecibido (#general, SOLO a Ana)
    H->>S: AddMensaje("#general", "Ana se ha unido")
    H-->>C: MensajeNuevo (al grupo #general)
    H-->>C: ListaUsuariosActualizada (#general)

    Note over C,H: Se cae la conexión

    H->>S: RemoveUsuario(conn-1)
    S-->>H: ConexionChat("#general", "Ana")
    H-->>C: MensajeNuevo "Ana ha salido" (al grupo #general)
    H-->>C: ListaUsuariosActualizada (#general)
```

> ⚠️ **Advertencia:** fíjate en el paso 14. Cuando Ana cambia de canal, el historial del nuevo canal se le envía **a ella sola** (`Clients.Caller`), mientras que los mensajes de sistema van al **grupo entero** (`Clients.Group`). Enviar el historial a todo el grupo saturaría a los demás, que no lo piden.

### 2.2. Alta: ConnectAsync

```csharp
public async Task<string?> ConnectAsync(string? usuario, string? canal = null)
```

- Si no se pasa canal, se entra al `CanalPorDefecto` (el primero de la lista de `appsettings.json`)
- El nombre se valida con `ChatConfig.ValidarUsuario` (longitud entre mínima y máxima)
- El registro en el store puede fallar si el nombre ya está en uso en ese canal

El nombre llega como parámetro de `ConnectAsync` y no en `OnConnectedAsync` porque **SignalR conecta primero y "quién eres" se dice después**: así el servidor puede rechazar nombres inválidos o duplicados devolviendo un mensaje de error al cliente.

### 2.3. Envío: SendAsync

```csharp
public async Task<string?> SendAsync(string? texto)
```

- Comprueba que la conexión esté registrada (una conexión "fantasma" que no se ha dado de alta no puede escribir)
- Valida el texto con `Config.ValidarMensaje` (no vacío, hasta `MensajeLongitudMaxima`)
- Guarda el mensaje en el historial de SU canal y lo difunde con `Clients.Group(conexion.Canal)`

Un detalle de buenas prácticas: **el log registra la longitud del mensaje, no el contenido**. Los logs pueden verlos personas no autorizadas.

### 2.4. Cambio de canal: CambiarCanalAsync

```csharp
public async Task<string?> CambiarCanalAsync(string? canal)
```

Este es el método que hace que Discord "sienta vivo": moverte entre canales sin reconectar. Los pasos, en orden:

1. Se comprueba que estés registrado (`GetConexion`) y que el canal exista (`EncontrarCanal`)
2. Se mueve el usuario en el store: alta en el destino (con la unicidad de nombre de ese canal) y liberación del nombre en el origen
3. Aviso de salida en el canal anterior, lista actualizada del anterior
4. Historial del nuevo canal SOLO al cliente que cambia
5. Aviso de entrada en el nuevo canal, lista actualizada del nuevo

Si el nombre ya está ocupado en el destino, el cambio se rechaza con un error y **te quedas donde estabas**.

### 2.5. Desconexión: OnDisconnectedAsync

SignalR llama a `OnDisconnectedAsync` cuando se corta la conexión (el cliente cierra la pestaña, se va el wifi...). El hub elimina al usuario del store y emite el aviso de salida **solo a su canal**:

```csharp
public override async Task OnDisconnectedAsync(Exception? exception)
{
    var conexion = store.RemoveUsuario(Context.ConnectionId);

    if (conexion is not null)
    {
        var mensaje = MensajesSistema.UsuarioSalio(conexion.Usuario);
        store.AddMensaje(conexion.Canal, mensaje);

        await Clients.Group(conexion.Canal).SendAsync(ChatHubEventos.MensajeNuevo, mensaje);
        await Clients.Group(conexion.Canal).SendAsync(
            ChatHubEventos.ListaUsuariosActualizada, store.GetUsuarios(conexion.Canal));
    }

    await base.OnDisconnectedAsync(exception);
}
```

Si la excepción no es `null`, es que la conexión cayó por un error (se registra como warning con la excepción). Si es `null`, es una desconexión limpia (se registra como información).

### 2.6. Cómo se prueba todo esto sin abrir una terminal

La clave está en que **el hub depende de interfaces**: `IChatStore` (el estado) e `IHubCallerClients` (a quién emitir). Con Moq simulamos ambas, y con los *setters* públicos inyectamos el doble de contexto:

```csharp
var hub = new ChatHub(store, Options.Create(config), logger.Object)
{
    Context = Mock.Of<HubCallerContext>(c => c.ConnectionId == "conn-1"),
    Clients = clientes.Object   // proxies que GRABAN lo que se emite
};
```

Los proxies no hacen nada de red: solo guardan en una lista cada `SendCoreAsync`. Así un test puede preguntar: "¿qué recibió el grupo #daw?" y "¿recibió algo el grupo #general?". En `ChatHubTests.cs` tienes el patrón completo, incluida la simulación de varias conexiones.

## 3. El estado en memoria: ChatStore

### 3.1. Dos estructuras, dos misiones

El estado del chat vive en memoria, en dos colecciones concurrentes con misiones distintas:

```mermaid
flowchart LR
    subgraph INDICE["_conexiones (índice global, uno solo)"]
        direction TB
        C1["conn-1 → #general · Ana"]
        C2["conn-2 → #daw · Luis"]
        C3["conn-3 → #daw · Marta"]
    end

    subgraph CANALES["_canales (un EstadoCanal por canal)"]
        direction TB
        G["#general<br/>nombres: Ana<br/>historial: cola"]
        S["#daw<br/>nombres: Luis, Marta<br/>historial: cola"]
    end

    C1 -.->|"vive en"| G
    C2 -.->|"viven en"| S
    C3 -.->|"viven en"| S

    style INDICE fill:#607D8B,color:#fff
    style CANALES fill:#9C27B0,color:#fff
    style G fill:#4CAF50,color:#fff
    style S fill:#4CAF50,color:#fff
```

| Estructura | Contenido | Por qué |
|------------|-----------|---------|
| `_conexiones` | `connectionId` → canal + usuario | Única fuente de verdad de "quién está conectado"; acceso en O(1) |
| `_canales` | canal → `EstadoCanal` (nombres + historial) | Unicidad de nombres e historial **separados por canal** |

Los nombres de canal y los usuarios se guardan con `ConcurrentDictionary`, pensada para que múltiples hilos (peticiones) lean y escriban a la vez sin bloquearse.

### 3.2. Unicidad de nombres sin bloqueos

Si dos personas intentan llamarse "Ana" a la vez en #general, **solo una puede ganar**. La clave no es un bloqueo tradicional, sino `TryAdd`, que es atómico: la operación entera es "o la hace alguien o la hace otro, nunca las dos":

```csharp
// TryAdd atómico POR CANAL: si alguien llegó primero con ese nombre
// en este canal, se rechaza sin necesidad de bloquear nada
if (!estado.UsuariosPorNombre.TryAdd(clave, nuevo))
    return Config.ErrorUsuarioDuplicado;
```

📌 **Ejemplo real:** Discord. Si intentas cambiar el nick de tu servidor a un nombre ya usado por otro miembro, te lo rechaza. En nuestro caso la regla es más laxa: la unicidad es **por canal**, así que puede haber una "Ana" en #general y otra distinta en #daw.

> 📝 **Nota:** las claves se normalizan con `ToUpperInvariant()` antes de comparar, así que "Ana", "ANA" y "ana" son la misma persona. El mismo truco se aplica a los canales (ver sección 4.3).

### 3.3. La cola circular del historial

Cada `EstadoCanal` guarda sus últimos N mensajes en una `ConcurrentQueue`. Al añadir uno nuevo, si la cola supera el límite configurado, se descarta el más antiguo:

```csharp
estado.Historial.Enqueue(mensaje);

// Cola circular: al superar el límite configurado, el más antiguo
// sale del historial.
while (estado.Historial.Count > Config.HistorialMaximo
       && estado.Historial.TryDequeue(out _))
{
}
```

📌 **Ejemplo real:** Discord te muestra los últimos 100 mensajes de un canal; más atrás, no hay nada (eso es historial persistido, que nuestra Fase 2 añadirá).

## 4. Los canales como grupos de SignalR

### 4.1. Un canal, un grupo

SignalR tiene un concepto nativo llamado **grupos**: conjuntos de conexiones a las que puedes enviar mensajes de golpe. Un canal de AulaChatNet **es exactamente un grupo de SignalR**:

```mermaid
flowchart TD
    subgraph CANAL_DAW["Canal #daw = grupo de SignalR"]
        T["Terminal<br/>(Ana)"]
        B["Blazor<br/>(Luis)"]
    end

    subgraph CANAL_GENERAL["Canal #general"]
        T2["Terminal<br/>(Marta)"]
    end

    H["ChatHub.SendAsync<br/>Clients.Group('#daw')"] -->|"MensajeNuevo"| T
    H -->|"MensajeNuevo"| B
    H -.->|"no llega nada aquí"| T2

    style H fill:#2196F3,color:#fff
    style T fill:#FF9800,color:#fff
    style B fill:#FF9800,color:#fff
    style T2 fill:#607D8B,color:#fff
    style CANAL_DAW fill:#4CAF50,color:#fff
    style CANAL_GENERAL fill:#607D8B,color:#fff
```

La belleza de esto es que **no hay que inventar la lógica de reenvío**: SignalR ya sabe a quién está enviando dentro del grupo, y quien se desconecta sale del grupo automáticamente.

### 4.2. Configuración sin hardcodeos

Los canales NO están escritos en el código: viven en `appsettings.json`, en la sección `Chat`:

```json
"Chat": {
  "Canales": [ "#general", "#smr", "#daw", "#asir", "#dam" ],
  "HubRuta": "/hubs/chat",
  "HistorialMaximo": 25
}
```

Y en `appsettings.Development.json` se puede sobreescribir para desarrollo:

```json
"Chat": {
  "HistorialMaximo": 50
}
```

La configuración se carga en un objeto tipado `ChatConfig` (opciones de .NET) que se inyecta donde haga falta (hub, store...). Añadir el canal "#erli" a un aula es añadir una línea al JSON, sin tocar ni recompilar una línea de C#.

### 4.3. Normalización: daw, #daw y #DAW

Los usuarios escriben a su manera: "daw", "#daw", "#DAW", " #daw ". Para que todo el mundo caiga en el mismo canal, `ChatConfig.NormalizarCanal` recorta espacios, añade la almohadilla si falta y pasa a mayúsculas. Después, `EncontrarCanal` devuelve la forma canónica (la que aparece en la configuración) para usarla como clave interna.

> 💡 **Consejo:** cuando el cliente terminal permita escribir `/canal daw`, esta normalización hará que funcione igual que `/canal #daw` o `/canal #DAW`.

## 5. Logging con Serilog desde appsettings

### 5.1. Consola para ver, fichero para investigar

El logging se configura **entero** en `appsettings.json`, sin tocar código: la sección `Serilog` describe dos destinos (sinks):

```mermaid
flowchart LR
    L["ILogger del servidor<br/>(ChatHub, Program...)"] --> SL["Serilog"]
    SL -->|"Information +<br/>(casi todo)"| CO["Consola<br/>con color, para ver"]
    SL -->|"solo Error<br/>y Fatal"| FI["logs/aulachat-YYYY-MM-DD.log<br/>rolling diario, retiene 7"]

    style L fill:#2196F3,color:#fff
    style SL fill:#9C27B0,color:#fff
    style CO fill:#4CAF50,color:#fff
    style FI fill:#f44336,color:#fff
```

| Destino | Nivel mínimo | Detalle |
|---------|--------------|---------|
| Consola | Information | Se ve casi todo; las peticiones HTTP a `/hubs` se bajan a Debug para no ensuciar |
| Fichero | Error | Solo errores, en `logs/aulachat-YYYY-MM-DD.log` |

### 5.2. El símbolo doble dos puntos de Serilog

Hay un detalle de sintaxis en el JSON que desconcierta al principio: el tema de la consola se referencia así:

```json
"theme": "Serilog.Sinks.SystemConsole.Themes.AnsiConsoleTheme::Code, Serilog.Sinks.Console"
```

Ese `::` es la forma de que Serilog se refiera a un **miembro estático** de un tipo de un ensamblado externo: `Tipo::Miembro, Ensamblado`. Es como si el JSON hiciera lo mismo que en C#: `AnsiConsoleTheme.Code`.

### 5.3. Rotación de 7 días

El fichero de errores usa rotación diaria (`rollingInterval: Day`) y retiene un máximo de 7 ficheros (`retainedFileCountLimit: 7`). Además, si un fichero pasa de 10 MB, rueda antes (`rollOnFileSizeLimit`). El resultado es que `logs/` nunca crece sin control: siempre tendrás los últimos 7 días de errores.

> 📝 **Nota:** la carpeta `logs/` está en `.gitignore`. Los logs son de ejecución, no del repositorio.

## 6. Dos bugs reales del servidor

No todo fue suave. Estos dos fallos aparecieron mientras se construía el servidor, y ambos son material de estudio.

### 6.1. La carrera de los diez Anas

**El problema.** Imagina diez personas entrando a la vez a un mismo canal y todas llamándose "Ana". Sin protección, el resultado sería imprevisible. Este es el test que lo reproduce:

```csharp
// Diez conexiones intentan registrarse como "Ana" A LA VEZ
Parallel.For(0, 10, i => resultados[i] = _store.AddUsuario(General, "Ana", $"conn-{i}"));
```

**La solución.** `ConcurrentDictionary.TryAdd` es atómico: de las diez, exactamente una gana el `TryAdd` del índice de nombres, y las nueve restantes reciben el error de duplicado. No hay bloqueos, no hay esperas: la operación completa es "todo o nada".

📌 **Ejemplo real:** el mostrador de un bar. Dos clientes llegan a la vez a pedir la última croqueta: el cajero atiende a uno primero y al otro le dice que ya no queda. No hace falta cerrar el bar entero.

### 6.2. El ConfigurationBinder que duplica listas

**El problema.** Al arrancar el servidor, el banner de inicio mostraba los canales dos veces:

```
Disponibles: #general, #smr, #daw, #asir, #dam, #general, #smr, #daw, #asir, #dam
```

¿Por qué? **El `ConfigurationBinder` de .NET añade los valores de `appsettings.json` a la lista existente en lugar de reemplazarla**. Si la propiedad `Canales` arrancaba con una lista por defecto de 5 canales, y el JSON traía otros 5, el resultado eran 10.

**La solución.** La propiedad arranca **vacía a propósito**:

```csharp
/// <remarks>
/// Arranca VACÍA a propósito: el ConfigurationBinder de .NET AÑADE los
/// valores de appsettings a la lista existente en lugar de reemplazarla,
/// así que una lista con valores por defecto los duplicaría con los del
/// fichero.
/// </remarks>
public List<string> Canales { get; set; } = [];
```

> ⚠️ **Advertencia:** este patrón de "inicializar vacío" aplica a **cualquier lista o diccionario** que se cargue desde configuración. Es un clásico de ASP.NET Core, y el error es tan silencioso que puede vivir meses en producción sin que nadie lo note.

📌 **Ejemplo real:** el equivalente del "doble envío" en formularios web: si tu validador "añade" en vez de "reemplazar", acabas con registros duplicados.

## 7. Resumen del punto

| Concepto | Qué retener |
|----------|-------------|
| Arquitectura | Hub (transporte) → Store (estado) → Shared (contrato compartido) |
| Errores | `Task<string?>`: `null` = OK, texto = mensaje para el cliente |
| Ciclo de vida | Conectar → alta → historial (solo al caller) → mensajes al grupo → salida |
| Estado | `_conexiones` global + un `EstadoCanal` (nombres + historial) por canal |
| Unicidad | `TryAdd` atómico por canal, con claves normalizadas |
| Historial | `ConcurrentQueue` circular, límite `HistorialMaximo` configurable |
| Canales | Cada canal es un grupo de SignalR; lista en `appsettings.json` |
| Logging | Serilog desde `appsettings.json`; consola Information, fichero Error con rolling de 7 días |
| Bugs reales | Carreras de concurrencia y el ConfigurationBinder que duplica listas |

En el siguiente punto pasamos a los clientes: primero el terminal con Spectre.Console y capa reactiva con Rx, y después la web con Blazor WebAssembly y su estética de Discord.
