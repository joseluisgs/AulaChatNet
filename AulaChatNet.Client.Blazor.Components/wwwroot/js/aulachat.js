// Utilidades mínimas de la UI en JavaScript puro (sin librerías): el scroll
// de la lista de mensajes y el foco del cuadro de texto. Blazor las llama con
// JSInterop desde los componentes.
//
// Este fichero vive en la Razor Class Library (wwwroot/js) y no en el
// index.html de cada host: así la web (WebAssembly) y la app de escritorio
// (WPF con BlazorWebView) comparten el MISMO script, cargado con la ruta de
// contenido estático _content/AulaChatNet.Client.Blazor.Components/js/aulachat.js. En
// Blazor Hybrid hay DOM de verdad, así que todo esto funciona igual.
window.aulachat = {
    abajo: function (id) {
        var lista = document.getElementById(id);
        if (lista) lista.scrollTop = lista.scrollHeight;
    },
    enfocar: function (id) {
        var campo = document.getElementById(id);
        if (campo) campo.focus();
    },

    // Paginación del historial (Fase 4.3): tres ayudantes. Altura
    // total del contenido y posición del scroll, para medir antes de
    // cargar; y recuperarScroll, que compensa el crecimiento que la
    // página vieja ha hecho POR ARRIBA del lector (delta = altura
    // nueva - altura medida): si no ha crecido, no mueve nada
    altura: function (id) {
        var lista = document.getElementById(id);
        return lista ? lista.scrollHeight : 0;
    },
    yPosicion: function (id) {
        var lista = document.getElementById(id);
        return lista ? lista.scrollTop : 0;
    },
    recuperarScroll: function (id, alturaAntes) {
        var lista = document.getElementById(id);
        if (lista) lista.scrollTop += lista.scrollHeight - alturaAntes;
    },

    // Parpadeo del título de la pestaña cuando alguien te menciona:
    // alterna el título con el aviso; se PARA al volver a la pestaña
    // (focus) o al dar el badge por visto (terminarParpadeo). Un solo
    // parpadeo a la vez
    parpadeoTitulo: null,
    tituloOriginal: null,
    empezarParpadeo: function (texto) {
        if (window.aulachat.parpadeoTitulo) return;

        window.aulachat.tituloOriginal = document.title;
        var original = window.aulachat.tituloOriginal;
        var alterna = false;

        window.aulachat.parpadeoTitulo = setInterval(function () {
            alterna = !alterna;
            document.title = alterna ? texto : original;
        }, 1200);

        window.addEventListener('focus', window.aulachat.terminarParpadeo);
    },
    terminarParpadeo: function () {
        if (!window.aulachat.parpadeoTitulo) return;

        clearInterval(window.aulachat.parpadeoTitulo);
        window.aulachat.parpadeoTitulo = null;
        document.title = window.aulachat.tituloOriginal;
        window.removeEventListener('focus', window.aulachat.terminarParpadeo);
    }
};
