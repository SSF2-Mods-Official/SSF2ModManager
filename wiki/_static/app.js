(function () {
  var content = document.getElementById("content");
  var nav = document.getElementById("sidebar-nav");
  var brandSub = document.querySelector(".brand .sub");

  function docs() {
    return window.SSF2_DOCS || null;
  }

  function route() {
    var raw = (location.hash || "#/").replace(/^#\/?/, "");
    if (!raw) return showHome();
    var anchor = "";
    var cut = raw.indexOf("#");
    if (cut >= 0) {
      anchor = decodeURIComponent(raw.slice(cut + 1));
      raw = raw.slice(0, cut);
    }
    return showPage(decodeURIComponent(raw), anchor);
  }

  function showError(msg) {
    content.innerHTML =
      "<h1>Docs error</h1><div class='warning'><p>" +
      msg +
      "</p><p>From <code>shell-dev\\docs</code> double-click <code>Open Docs.bat</code> to rebuild.</p></div>";
  }

  var QUESTIONS = [
    ["how do I set up", "Start Here/setup.md"],
    ["how do I join the discord", "Start Here/discord.md"],
    ["how do I download SSF2", "Start Here/setup.md"],
    ["how do I play", "Mod Manager Features/play.md"],
    ["how do I download mods", "Mod Manager Features/download-mods.md"],
    ["how do I configure a match", "Mod Manager Features/configure.md"],
    ["how do I change how the game starts", "Mod Manager Features/configure.md"],
    ["how do I make a mod", "Mod Manager Features/make-mods.md"],
    ["how do I turn mods on", "Mod Manager Features/manage-mods.md"],
    ["how do I use the music player", "Mod Manager Features/music-player.md"],
    ["how do I change settings", "Mod Manager Features/settings.md"],
    ["how do I debug", "Mod Manager Features/debug.md"],
    ["how do I open the jukebox", "SSF2 Shell Features/my-jukebox.md"],
    ["how do I add music to my build", "SSF2 Shell Features/my-jukebox.md"],
    ["how do I upload a song", "SSF2 Shell Features/my-jukebox.md"],
    ["how do I add a song to a stage", "SSF2 Shell Features/my-jukebox.md"],
    ["how do I add a character", "Mod Manager Features/add-character.md"],
    ["how do I clone a character", "Mod Manager Features/add-character.md"],
    ["how do I import a character", "Mod Manager Features/add-character.md"],
    ["how do I add a stage", "Mod Manager Features/add-stage.md"],
    ["how do I clone a stage", "Mod Manager Features/add-stage.md"],
    ["how do I import a stage", "Mod Manager Features/add-stage.md"],
    ["how do I add an event", "Mod Manager Features/add-event.md"],
    ["how do I make an event", "Mod Manager Features/add-event.md"],
    ["how do I package a mod", "Mod Manager Features/package-mods.md"],
    ["how do I upload a mod to GameBanana", "Mod Manager Features/package-mods.md"],
    ["how do I set a nametag", "SSF2 Shell Features/nametags.md"],
    ["how do I add a stage background", "SSF2 Shell Features/stage-backgrounds.md"],
    ["how do I use palette maker", "SSF2 Shell Features/palette-maker.md"],
    ["how do I recolor a character", "SSF2 Shell Features/palette-maker.md"],
    ["how do I watch a replay", "SSF2 Shell Features/replay-menu.md"],
    ["how do I change the menu background", "SSF2 Shell Features/menu-background.md"],
    ["how do I change the camera background", "SSF2 Shell Features/camera-background.md"],
    ["how do I open the mod menu", "SSF2 Shell Features/mod-menu.md"],
    ["how do I edit the character select screen", "SSF2 Shell Features/character-select-editor.md"],
    ["how do I edit the stage select screen", "SSF2 Shell Features/stage-select-editor.md"],
    ["how do I turn on rumble", "SSF2 Shell Features/rumble.md"]
  ];

  function localStore() {
    try { return JSON.parse(localStorage.getItem("ssf2-wiki-local") || "{}"); }
    catch (e) { return {}; }
  }

  function saveLocal(store) {
    localStorage.setItem("ssf2-wiki-local", JSON.stringify(store));
  }

  function loadBundle() {
    var data = docs();
    if (!data || !data.sections) {
      throw new Error("content.js missing or empty");
    }
    data = JSON.parse(JSON.stringify(data));
    data.pages = data.pages || {};
    var store = localStore();
    var mine = [];
    Object.keys(store).forEach(function (file) {
      var item = store[file];
      if (!item || !item.md) return;
      data.pages[file] = item.md;
      var title = "My article";
      var m = item.md.match(/^title:\s*(.+)$/m);
      if (m) title = m[1].trim();
      mine.push({ file: file, title: title, summary: "Saved on this computer.", order: 10 });
    });
    if (mine.length) {
      data.sections = data.sections.concat([{ id: "My Tutorials", title: "My Tutorials", pages: mine }]);
    }
    return data;
  }

  function buildNav(manifest, currentFile) {
    var html = "";
    html += '<a href="#/"' + (!currentFile ? ' class="current"' : "") + ">Home</a>";
    (manifest.sections || []).forEach(function (sec) {
      html += '<div class="section">' + escape(sec.title) + "</div>";
      (sec.pages || []).forEach(function (p) {
        var cur = p.file === currentFile ? ' class="current"' : "";
        html +=
          '<a href="#/' +
          encodeURIComponent(p.file) +
          '"' +
          cur +
          ">" +
          escape(p.title) +
          "</a>";
      });
    });
    nav.innerHTML = html;
  }

  function escape(s) {
    return String(s)
      .replace(/&/g, "&amp;")
      .replace(/</g, "&lt;")
      .replace(/>/g, "&gt;")
      .replace(/"/g, "&quot;");
  }

  function showHome() {
    try {
      var manifest = loadBundle();
      buildNav(manifest, null);
      if (brandSub) brandSub.textContent = "Wiki";
      document.title = "SSF2 Mod Manager Wiki";
      var parts = [];
      parts.push("<h1>SSF2 Mod Manager Wiki</h1>");
      parts.push(
        '<p class="lead">Guides for the Mod Manager and for the features SSF2 Shell adds on top of the normal game.</p>'
      );
      parts.push(
        '<div class="tip"><strong>Get the build</strong> from ' +
          '<a href="https://github.com/SSF2-Mods-Official/SSF2ModManager/releases/latest">GitHub Releases (latest)</a>.</div>'
      );
      parts.push(
        '<div class="tip"><strong>Discord.</strong> <a href="#/' +
          encodeURIComponent("Start Here/discord.md") +
          '">Join SSF2 Mods Official</a>.</div>'
      );
      parts.push(
        '<div class="warning"><strong>WARNING.</strong> The SSF2 Beta Mod Manager is in a beta release state. Features still need to be tested and improved on before being considered stable.</div>'
      );
      parts.push(
        "<p>We are looking for developers to help contribute to the Mod Manager and SSF2 Shell. Reach out to justwex on the SSF2 Mods Official discord if you are interested in contributing.</p>"
      );

      (manifest.sections || []).forEach(function (sec) {
        parts.push("<h2>" + escape(sec.title) + "</h2>");
        parts.push('<div class="cards">');
        (sec.pages || []).forEach(function (p) {
          parts.push(
            '<a class="card" href="#/' +
              encodeURIComponent(p.file) +
              '"><strong>' +
              escape(p.title) +
              "</strong><span>" +
              escape(p.summary || "") +
              "</span></a>"
          );
        });
        parts.push("</div>");
      });

      if (!(manifest.sections || []).length) {
        parts.push(
          '<div class="warning"><p>No doc sections found. Add folders like <code>Start Here</code> with <code>.md</code> files, then run <code>Open Docs.bat</code>.</p></div>'
        );
      }

      content.innerHTML = parts.join("\n");
    } catch (err) {
      showError(escape(err.message || String(err)));
    }
  }

  function showPage(file, anchor) {
    try {
      var manifest = loadBundle();
      var raw = (manifest.pages && manifest.pages[file]) || null;
      if (raw == null) {
        throw new Error("Unknown page: " + file + " — run Open Docs.bat after adding .md files");
      }
      buildNav(manifest, file);
      renderArticle(file, raw);
      var target = anchor ? document.getElementById(anchor) : null;
      if (target) target.scrollIntoView();
      else window.scrollTo(0, 0);
    } catch (err) {
      showError(escape(err.message || String(err)));
    }
  }

  function renderArticle(file, raw) {
    var parsed = Ssf2DocsMd.parseFrontMatter(raw);
    var title = parsed.meta.title || file;
    if (brandSub) brandSub.textContent = title;
    document.title = title + " — SSF2 Mod Manager Wiki";
    var bodyHtml = Ssf2DocsMd.renderMarkdown(parsed.body);
    content.innerHTML =
      '<article class="article"><h1>' +
      escape(title) +
      "</h1>" +
      (parsed.meta.summary ? '<p class="lead">' + escape(parsed.meta.summary) + "</p>" : "") +
      bodyHtml +
      "</article>";
    var h1s = content.querySelectorAll("article h1");
    if (h1s.length > 1) h1s[1].remove();
    fixLinks(file);
    if (window.wiki) mountPageFoot(file, raw);
    else mountSiteFoot(file, raw);
    if (sessionStorage.getItem("wiki-open-edit") === file) {
      sessionStorage.removeItem("wiki-open-edit");
      openEditor(file, raw);
    }
  }

  function mountSiteFoot(file, raw) {
    var item = localStore()[file];
    var record = item
      ? { userMade: true, entries: item.entries && item.entries.length ? item.entries : [{ author: "You", note: "Created", at: "" }] }
      : { userMade: false, entries: [] };
    var foot = document.createElement("div");
    foot.className = "page-foot";
    var html = '<section class="history">' + historyHtml(versionRows(record)) + "</section>";
    if (item) {
      html += '<div class="page-actions"><button type="button" id="edit-page">Edit</button>';
      html += '<button type="button" id="dl-page">Download</button>';
      html += '<button type="button" class="danger" id="del-page">Delete</button></div>';
    }
    foot.innerHTML = html;
    content.appendChild(foot);
    var edit = foot.querySelector("#edit-page");
    if (edit) edit.addEventListener("click", function () { openEditor(file, raw); });
    var dl = foot.querySelector("#dl-page");
    if (dl) dl.addEventListener("click", function () { downloadArticle(file, raw); });
    var del = foot.querySelector("#del-page");
    if (del) {
      del.addEventListener("click", function () {
        askConfirm("Delete this page?", "Delete").then(function (ok) {
          if (!ok) return;
          var store = localStore();
          delete store[file];
          saveLocal(store);
          if (location.hash === "#/" || location.hash === "") route();
          else location.hash = "#/";
        });
      });
    }
  }

  function pictureSrc(file, rel) {
    var name = String(rel || "").split("/").pop().split("\\").pop();
    var item = localStore()[file];
    if (item && item.images && item.images[name]) return item.images[name];
    var enc = encodeURIComponent(name);
    if (location.protocol === "file:") return "../_images/" + enc;
    return "images/" + enc;
  }

  function fixLinks(file) {
    var baseDir = file.replace(/[^/]+$/, "");
    content.querySelectorAll("img[data-rel]").forEach(function (img) {
      img.setAttribute("src", pictureSrc(file, img.getAttribute("data-rel") || ""));
    });
    content.querySelectorAll('a[href^="#md-rel:"]').forEach(function (a) {
      var rel = decodeURIComponent(a.getAttribute("href").slice(8));
      a.setAttribute("href", "#/" + encodeURIComponent(resolveRel(baseDir, rel)));
    });
    content.querySelectorAll("a[href]").forEach(function (a) {
      var href = a.getAttribute("href") || "";
      if (href.charAt(0) === "#" && href.indexOf("#/") !== 0 && href.indexOf("#md-rel:") !== 0) {
        a.setAttribute("href", "#/" + encodeURIComponent(file) + href);
      }
    });
  }

  function resolveRel(baseDir, rel) {
    var parts = (baseDir + rel).split("/");
    var out = [];
    parts.forEach(function (p) {
      if (p === "" || p === ".") return;
      if (p === "..") out.pop();
      else out.push(p);
    });
    return out.join("/");
  }

  function versionRows(record) {
    var entries = (record && record.entries) || [];
    var userMade = !!(record && record.userMade);
    var rows = entries.map(function (row, i) {
      return {
        version: entries.length - i + (userMade ? 0 : 1),
        author: row.author,
        note: row.note,
        at: row.at
      };
    });
    if (!userMade) {
      rows.push({ version: 1, author: "Mod Manager", note: "Published", at: "" });
    }
    return rows;
  }

  function historyHtml(rows) {
    if (!rows.length) return "<h2>Changelog</h2><p>Not published yet.</p>";
    return (
      "<h2>Changelog</h2><ul>" +
      rows
        .map(function (row) {
          return (
            "<li><strong>Version " +
            row.version +
            "</strong> " +
            escape(row.author || "") +
            " — " +
            escape(row.note || "") +
            (row.at ? " <span>" + escape(row.at) + "</span>" : "") +
            "</li>"
          );
        })
        .join("") +
      "</ul>"
    );
  }

  function mountPageFoot(file, raw) {
    var foot = document.createElement("div");
    foot.className = "page-foot";
    foot.innerHTML = '<section class="history"><p>Loading history…</p></section>';
    content.appendChild(foot);
    window.wiki.history(file).then(function (record) {
      var rows = versionRows(record);
      var html = historyHtml(rows);
      html += '<div class="page-actions"><button type="button" id="edit-page">Edit</button>';
      if (record && record.userMade) {
        html += '<button type="button" id="pkg-page">Download</button>';
        html += '<button type="button" class="danger" id="del-page">Delete</button>';
      }
      html += "</div>";
      foot.innerHTML = html;
      foot.querySelector("#edit-page").addEventListener("click", function () {
        openEditor(file, raw);
      });
      var pkg = foot.querySelector("#pkg-page");
      if (pkg) {
        pkg.addEventListener("click", function () {
          window.wiki.author().then(function (author) {
            return window.wiki.packagePage(file, author || "");
          }).catch(function (err) {
            showError(escape(err && err.message ? err.message : String(err)));
          });
        });
      }
      var del = foot.querySelector("#del-page");
      if (del) {
        del.addEventListener("click", function () {
          askConfirm("Delete this page?", "Delete").then(function (ok) {
            if (!ok) return;
            window.wiki.deletePage(file).then(function () {
              location.hash = "#/";
              location.reload();
            }).catch(function (err) {
              showError(escape(err && err.message ? err.message : String(err)));
            });
          });
        });
      }
    });
  }

  function openEditor(file, raw) {
    content.innerHTML = "";
    var editor = document.createElement("div");
    editor.className = "md-editor";
    editor.innerHTML =
      '<div class="md-toolbar">' +
      '<div class="md-modes">' +
      '<button type="button" data-mode="write" class="on">Write</button>' +
      '<button type="button" data-mode="preview">Preview</button>' +
      "</div>" +
      '<div class="md-tools">' +
      '<button type="button" data-act="h1" title="Heading 1">H1</button>' +
      '<button type="button" data-act="h2" title="Heading 2">H2</button>' +
      '<button type="button" data-act="h3" title="Heading 3">H3</button>' +
      '<button type="button" data-act="bold" title="Bold"><b>B</b></button>' +
      '<button type="button" data-act="italic" title="Italic"><i>I</i></button>' +
      '<button type="button" data-act="strike" title="Strikethrough"><s>S</s></button>' +
      '<button type="button" data-act="quote" title="Quote">“”</button>' +
      '<button type="button" data-act="code" title="Inline code">&lt;/&gt;</button>' +
      '<button type="button" data-act="fence" title="Code block">{ }</button>' +
      '<button type="button" data-act="link" title="Link">Link</button>' +
      '<button type="button" data-act="image" title="Image">Image</button>' +
      '<select id="edit-images" title="Insert an image"><option value="">Images</option></select>' +
      '<button type="button" data-act="ul" title="Bullet list">• List</button>' +
      '<button type="button" data-act="ol" title="Numbered list">1. List</button>' +
      '<button type="button" data-act="hr" title="Horizontal rule">―</button>' +
      '<button type="button" data-act="table" title="Table">Table</button>' +
      "</div>" +
      "</div>" +
      '<textarea id="edit-body" spellcheck="false"></textarea>' +
      '<div class="md-preview article" hidden></div>' +
      '<div class="md-footer">' +
      '<button type="button" id="edit-cancel">Cancel</button>' +
      '<button type="button" id="edit-save">Save</button>' +
      "</div>";
    content.appendChild(editor);
    var box = editor.querySelector("#edit-body");
    box.value = raw;
    var known = (window.wiki && window.wiki.images)
      ? window.wiki.images()
      : Promise.resolve(Object.keys((localStore()[file] && localStore()[file].images) || {}));
    known.then(function (names) {
      var sel = editor.querySelector("#edit-images");
      (names || []).forEach(function (name) {
        var opt = document.createElement("option");
        opt.value = name;
        opt.textContent = name;
        sel.appendChild(opt);
      });
    });

    function setMode(mode) {
      var write = mode !== "preview";
      box.hidden = !write;
      var preview = editor.querySelector(".md-preview");
      preview.hidden = write;
      editor.querySelectorAll(".md-modes button").forEach(function (b) {
        b.classList.toggle("on", b.getAttribute("data-mode") === mode);
      });
      if (!write) {
        var parsed = Ssf2DocsMd.parseFrontMatter(box.value);
        var title = parsed.meta.title || file;
        preview.innerHTML =
          "<h1>" +
          escape(title) +
          "</h1>" +
          (parsed.meta.summary ? '<p class="lead">' + escape(parsed.meta.summary) + "</p>" : "") +
          Ssf2DocsMd.renderMarkdown(parsed.body);
        var h1s = preview.querySelectorAll("h1");
        if (h1s.length > 1) h1s[1].remove();
        preview.querySelectorAll("img[data-rel]").forEach(function (img) {
          img.setAttribute("src", pictureSrc(file, img.getAttribute("data-rel") || ""));
        });
      }
    }

    editor.querySelector(".md-modes").addEventListener("click", function (ev) {
      var mode = ev.target.getAttribute && ev.target.getAttribute("data-mode");
      if (mode) setMode(mode);
    });
    editor.querySelector(".md-tools").addEventListener("click", function (ev) {
      var act = ev.target.getAttribute && ev.target.getAttribute("data-act");
      if (!act && ev.target.parentElement) act = ev.target.parentElement.getAttribute("data-act");
      if (!act) return;
      applyAction(act);
    });
    editor.querySelector("#edit-images").addEventListener("change", function (ev) {
      if (!ev.target.value) return;
      insertImage(ev.target.value);
      ev.target.value = "";
    });
    editor.querySelector("#edit-cancel").addEventListener("click", function () {
      renderArticle(file, raw);
    });
    editor.querySelector("#edit-save").addEventListener("click", function () {
      if (!(window.wiki && window.wiki.savePage)) {
        askSiteSave().then(function (fields) {
          if (!fields) return;
          var store = localStore();
          var item = store[file] || { images: {} };
          item.md = box.value;
          item.entries = item.entries || [];
          item.entries.unshift({ at: new Date().toISOString(), author: fields.author, note: fields.note });
          store[file] = item;
          saveLocal(store);
          route();
        });
        return;
      }
      askSave().then(function (fields) {
        if (!fields) return;
        window.wiki
          .savePage(file, box.value, fields.author, fields.note)
          .then(function () {
            location.reload();
          })
          .catch(function (err) {
            showError(escape(err && err.message ? err.message : String(err)));
          });
      });
    });

    function applyAction(act) {
      if (act === "bold") return wrap("**", "**", "bold");
      if (act === "italic") return wrap("*", "*", "italic");
      if (act === "strike") return wrap("~~", "~~", "strike");
      if (act === "h1") return linePrefix("# ");
      if (act === "h2") return linePrefix("## ");
      if (act === "h3") return linePrefix("### ");
      if (act === "quote") return linePrefix("> ");
      if (act === "code") return wrap("`", "`", "code");
      if (act === "fence") return wrap("\n```\n", "\n```\n", "code");
      if (act === "link") return wrap("[", "](https://)", "text");
      if (act === "ul") return linePrefix("- ");
      if (act === "ol") return linePrefix("1. ");
      if (act === "hr") return insert("\n\n---\n\n");
      if (act === "table") {
        return insert("\n\n| Column | Column |\n| --- | --- |\n|  |  |\n\n");
      }
      if (act === "image") {
        if (window.wiki && window.wiki.pickImage) {
          window.wiki.pickImage().then(function (name) {
            if (!name) return;
            rememberImage(name);
            insertImage(name);
          });
          return;
        }
        var picker = document.createElement("input");
        picker.type = "file";
        picker.accept = "image/*";
        picker.addEventListener("change", function () {
          var picked = picker.files && picker.files[0];
          if (!picked) return;
          var reader = new FileReader();
          reader.onload = function () {
            var store = localStore();
            var item = store[file] || { md: box.value, images: {} };
            if (!item.images) item.images = {};
            item.images[picked.name] = reader.result;
            item.md = box.value;
            store[file] = item;
            saveLocal(store);
            rememberImage(picked.name);
            insertImage(picked.name);
          };
          reader.readAsDataURL(picked);
        });
        picker.click();
      }
    }

    function wrap(before, after, placeholder) {
      var start = box.selectionStart;
      var end = box.selectionEnd;
      var sel = box.value.slice(start, end) || placeholder;
      box.value = box.value.slice(0, start) + before + sel + after + box.value.slice(end);
      box.focus();
      box.selectionStart = start + before.length;
      box.selectionEnd = start + before.length + sel.length;
    }

    function linePrefix(prefix) {
      var start = box.selectionStart;
      var line = box.value.lastIndexOf("\n", start - 1) + 1;
      box.value = box.value.slice(0, line) + prefix + box.value.slice(line);
      box.focus();
      box.selectionStart = box.selectionEnd = start + prefix.length;
    }

    function insert(snippet) {
      var start = box.selectionStart;
      box.value = box.value.slice(0, start) + snippet + box.value.slice(start);
      box.focus();
      box.selectionStart = box.selectionEnd = start + snippet.length;
    }

    function rememberImage(name) {
      var sel = editor.querySelector("#edit-images");
      var opt = document.createElement("option");
      opt.value = name;
      opt.textContent = name;
      sel.appendChild(opt);
    }

    function insertImage(name) {
      var depth = file.split("/").length - 1;
      var rel = "../".repeat(Math.max(depth, 0)) + "_images/" + name;
      insert("\n![" + name + "](" + rel + ")\n");
    }
  }

  function askSiteSave() {
    var saved = "";
    try { saved = localStorage.getItem("ssf2-wiki-author") || ""; } catch (e) { saved = ""; }
    return askForm(
      "Save page",
      [
        { id: "author", label: "Author", value: saved },
        { id: "note", label: "What changed", value: "" }
      ],
      "Save"
    ).then(function (fields) {
      if (!fields) return null;
      if (!fields.author.trim() || !fields.note.trim()) {
        showError("Author and what changed are both required.");
        return null;
      }
      try { localStorage.setItem("ssf2-wiki-author", fields.author.trim()); } catch (e) {}
      return { author: fields.author.trim(), note: fields.note.trim() };
    });
  }

  function askSave() {
    return window.wiki.author().then(function (saved) {
      return askForm(
        "Save page",
        [
          { id: "author", label: "Author", value: saved || "" },
          { id: "note", label: "What changed", value: "" }
        ],
        "Save"
      ).then(function (fields) {
        if (!fields) return null;
        if (!fields.author.trim() || !fields.note.trim()) {
          showError("Author and what changed are both required.");
          return null;
        }
        return { author: fields.author.trim(), note: fields.note.trim() };
      });
    });
  }

  function askConfirm(title, okLabel) {
    return new Promise(function (resolve) {
      var back = document.createElement("div");
      back.className = "modal-back";
      back.innerHTML =
        '<form class="modal"><h2>' +
        escape(title) +
        '</h2><div class="modal-actions"><button type="button" class="ghost">Cancel</button><button type="submit" class="danger">' +
        escape(okLabel) +
        "</button></div></form>";
      document.body.appendChild(back);
      back.querySelector(".ghost").addEventListener("click", function () {
        back.remove();
        resolve(false);
      });
      back.querySelector("form").addEventListener("submit", function (ev) {
        ev.preventDefault();
        back.remove();
        resolve(true);
      });
    });
  }

  function askForm(title, fields, okLabel) {
    return new Promise(function (resolve) {
      var back = document.createElement("div");
      back.className = "modal-back";
      var html = '<form class="modal"><h2>' + escape(title) + "</h2>";
      fields.forEach(function (f) {
        if (f.type === "select") {
          html += "<label>" + escape(f.label) + '<select name="' + escape(f.id) + '">';
          (f.options || []).forEach(function (opt) {
            html += '<option value="' + escape(opt.id) + '">' + escape(opt.label) + "</option>";
          });
          html += "</select></label>";
          return;
        }
        html +=
          "<label>" +
          escape(f.label) +
          '<input name="' +
          escape(f.id) +
          '" value="' +
          escape(f.value || "") +
          '"></label>';
      });
      html +=
        '<div class="modal-actions"><button type="button" class="ghost">Cancel</button><button type="submit">' +
        escape(okLabel) +
        "</button></div></form>";
      back.innerHTML = html;
      document.body.appendChild(back);
      var form = back.querySelector("form");
      back.querySelector(".ghost").addEventListener("click", function () {
        back.remove();
        resolve(null);
      });
      form.addEventListener("submit", function (ev) {
        ev.preventDefault();
        var out = {};
        fields.forEach(function (f) {
          out[f.id] = form.elements[f.id].value;
        });
        back.remove();
        resolve(out);
      });
      var first = form.querySelector("input");
      if (first) first.focus();
    });
  }

  function crc32(bytes) {
    var table = crc32.table;
    if (!table) {
      table = crc32.table = new Uint32Array(256);
      for (var n = 0; n < 256; n++) {
        var c = n;
        for (var k = 0; k < 8; k++) c = (c & 1) ? (0xedb88320 ^ (c >>> 1)) : (c >>> 1);
        table[n] = c;
      }
    }
    var crc = 0xffffffff;
    for (var i = 0; i < bytes.length; i++) crc = table[(crc ^ bytes[i]) & 255] ^ (crc >>> 8);
    return (crc ^ 0xffffffff) >>> 0;
  }

  function u16(view, o, v) { view.setUint16(o, v, true); }
  function u32(view, o, v) { view.setUint32(o, v, true); }

  function zipStore(files) {
    var enc = new TextEncoder();
    var locals = [];
    var central = [];
    var offset = 0;
    files.forEach(function (f) {
      var name = enc.encode(f.name);
      var data = f.data;
      var crc = crc32(data);
      var local = new DataView(new ArrayBuffer(30));
      u32(local, 0, 0x04034b50);
      u16(local, 4, 20);
      u32(local, 14, crc);
      u32(local, 18, data.length);
      u32(local, 22, data.length);
      u16(local, 26, name.length);
      var cen = new DataView(new ArrayBuffer(46));
      u32(cen, 0, 0x02014b50);
      u16(cen, 4, 20);
      u16(cen, 6, 20);
      u32(cen, 16, crc);
      u32(cen, 20, data.length);
      u32(cen, 24, data.length);
      u16(cen, 28, name.length);
      u32(cen, 42, offset);
      locals.push(new Uint8Array(local.buffer), name, data);
      central.push(new Uint8Array(cen.buffer), name);
      offset += 30 + name.length + data.length;
    });
    var centralStart = offset;
    var centralSize = 0;
    central.forEach(function (p) { centralSize += p.length; });
    var end = new DataView(new ArrayBuffer(22));
    u32(end, 0, 0x06054b50);
    u16(end, 8, files.length);
    u16(end, 10, files.length);
    u32(end, 12, centralSize);
    u32(end, 16, centralStart);
    var chunks = locals.concat(central, [new Uint8Array(end.buffer)]);
    var total = chunks.reduce(function (n, p) { return n + p.length; }, 0);
    var out = new Uint8Array(total);
    var at = 0;
    chunks.forEach(function (p) { out.set(p, at); at += p.length; });
    return out;
  }

  function tutorialMarkdown(title, template) {
    var safe = String(title).replace(/[\r\n]/g, " ");
    if (template === "reference") {
      return "---\ntitle: " + safe + "\nsummary: One line on what this page is for.\norder: 50\n---\n\n# " + safe + "\n\n## Info\n\n| " + safe + " |\n| --- |\n| Field | Value |\n| Related | [Another page](another-page.md) |\n\nOne sentence on what this page is for.\n\n## Contents\n\n1. [How it works](#how-it-works)\n2. [References](#references)\n\n## How it works\n\n| Function | What it does | Log line |\n| --- | --- | --- |\n| `name` | What it does. | `log line` |\n\n## References\n\n1. [Another page](another-page.md) — what that page is for.\n";
    }
    return "---\ntitle: " + safe + "\nsummary: One line on what this page is for.\norder: 10\n---\n\nOne sentence on what the reader does here.\n\n## First step\n\nWrite the step.\n\n## Next step\n\nWrite the next step.\n";
  }

  function downloadArticle(file, raw) {
    var store = localStore()[file] || {};
    var files = [{ name: "article.md", data: new TextEncoder().encode(raw) }];
    var images = store.images || {};
    Object.keys(images).forEach(function (name) {
      var m = String(images[name]).match(/^data:[^;]+;base64,(.+)$/);
      if (!m) return;
      var bin = atob(m[1]);
      var bytes = new Uint8Array(bin.length);
      for (var i = 0; i < bin.length; i++) bytes[i] = bin.charCodeAt(i);
      files.push({ name: "images/" + name, data: bytes });
    });
    var blob = new Blob([zipStore(files)], { type: "application/zip" });
    var a = document.createElement("a");
    a.href = URL.createObjectURL(blob);
    a.download = (file.split("/").pop() || "article").replace(/\.md$/, "") + ".zip";
    a.click();
    setTimeout(function () { URL.revokeObjectURL(a.href); }, 1000);
  }

  function findLocal(bytes, sig) {
    for (var i = 0; i < bytes.length - 4; i++) {
      if (bytes[i] === sig[0] && bytes[i + 1] === sig[1] && bytes[i + 2] === sig[2] && bytes[i + 3] === sig[3])
        return i;
    }
    return -1;
  }

  async function unzip(bytes) {
    var out = [];
    var i = 0;
    while (i + 30 < bytes.length) {
      if (bytes[i] !== 0x50 || bytes[i + 1] !== 0x4b || bytes[i + 2] !== 0x03 || bytes[i + 3] !== 0x04) break;
      var view = new DataView(bytes.buffer, bytes.byteOffset + i);
      var method = view.getUint16(8, true);
      var comp = view.getUint32(18, true);
      var nameLen = view.getUint16(26, true);
      var extra = view.getUint16(28, true);
      var name = new TextDecoder().decode(bytes.subarray(i + 30, i + 30 + nameLen));
      var start = i + 30 + nameLen + extra;
      var data = bytes.subarray(start, start + comp);
      if (method === 8 && typeof DecompressionStream !== "undefined") {
        var ds = new DecompressionStream("deflate-raw");
        var stream = new Blob([data]).stream().pipeThrough(ds);
        data = new Uint8Array(await new Response(stream).arrayBuffer());
      }
      if (name && !name.endsWith("/")) out.push({ name: name, data: data });
      i = start + comp;
    }
    return out;
  }

  function mountCorner() {
    var search = document.getElementById("wiki-search");
    var hits = document.getElementById("wiki-hits");

    function exactFile(text) {
      var q = (text || "").trim().toLowerCase();
      if (!q) return "";
      for (var i = 0; i < QUESTIONS.length; i++) {
        if (QUESTIONS[i][0].toLowerCase() === q) return QUESTIONS[i][1];
      }
      var found = "";
      try {
        var bundle = loadBundle();
        (bundle.sections || []).forEach(function (sec) {
          (sec.pages || []).forEach(function (p) {
            if (!found && (p.title || "").toLowerCase() === q) found = p.file;
          });
        });
      } catch (e2) { /* ignore */ }
      return found;
    }

    function bestFile(text) {
      var exact = exactFile(text);
      if (exact) return exact;
      var q = (text || "").trim().toLowerCase();
      if (q.length < 2) return "";
      var best = null;
      QUESTIONS.forEach(function (row) {
        if (row[0].toLowerCase().indexOf(q) >= 0 && (!best || row[0].length < best[0].length)) best = row;
      });
      return best ? best[1] : "";
    }

    function openFile(file) {
      if (!file) return;
      var next = "#/" + encodeURIComponent(file);
      if (location.hash !== next) location.hash = next;
      if (hits) hits.innerHTML = "";
      route();
    }

    function matchingRows(q) {
      var rows = [];
      var seen = {};
      function add(label, file) {
        var key = label.toLowerCase();
        if (!label || seen[key]) return;
        seen[key] = 1;
        rows.push({ label: label, file: file });
      }
      QUESTIONS.forEach(function (row) {
        if (!q || row[0].toLowerCase().indexOf(q) >= 0) add(row[0], row[1]);
      });
      if (q) {
        try {
          var manifest = loadBundle();
          (manifest.sections || []).forEach(function (sec) {
            (sec.pages || []).forEach(function (p) {
              if ((p.title || "").toLowerCase().indexOf(q) >= 0) add(p.title, p.file);
            });
          });
        } catch (e) { /* bundle not ready */ }
      }
      return rows;
    }

    function showHits(q) {
      if (!hits) return;
      hits.innerHTML = "";
      matchingRows(q).forEach(function (row) {
        var b = document.createElement("button");
        b.type = "button";
        b.textContent = row.label;
        b.addEventListener("mousedown", function (ev) {
          ev.preventDefault();
          openFile(row.file);
        });
        hits.appendChild(b);
      });
    }

    if (search) {
      search.addEventListener("focus", function () {
        showHits(search.value.trim().toLowerCase());
      });
      search.addEventListener("input", function () {
        var typed = search.value.trim().toLowerCase();
        var exact = exactFile(search.value);
        if (exact) openFile(exact);
        else showHits(typed);
      });
      search.addEventListener("change", function () {
        openFile(exactFile(search.value) || bestFile(search.value));
      });
      search.addEventListener("keydown", function (ev) {
        if (ev.key === "Enter") {
          ev.preventDefault();
          openFile(exactFile(search.value) || bestFile(search.value));
        }
        if (ev.key === "Escape" && hits) hits.innerHTML = "";
      });
      document.addEventListener("mousedown", function (ev) {
        if (!hits || !hits.innerHTML) return;
        var t = ev.target;
        if (t === search || (hits.contains && hits.contains(t))) return;
        hits.innerHTML = "";
      });
    }

    var neu = document.getElementById("new-tutorial");
    if (neu) neu.addEventListener("click", function () {
      askForm("New tutorial", [
        { id: "title", label: "Title", value: "" },
        { id: "template", label: "Template", type: "select", options: [
          { id: "article", label: "Article" },
          { id: "reference", label: "Reference" }
        ] }
      ], "Create").then(function (fields) {
        if (!fields || !fields.title.trim()) return;
        var title = fields.title.trim();
        var template = fields.template === "reference" ? "reference" : "article";
        if (window.wiki && window.wiki.createTutorial) {
          window.wiki.createTutorial(title, tutorialMarkdown(title, template)).then(function (file) {
            sessionStorage.setItem("wiki-open-edit", file);
            location.hash = "#/" + encodeURIComponent(file);
            location.reload();
          }).catch(function (err) {
            showError(escape(err && err.message ? err.message : String(err)));
          });
          return;
        }
        var slug = title.toLowerCase().replace(/[^a-z0-9]+/g, "-").replace(/^-|-$/g, "") || "tutorial";
        var file = "My Tutorials/" + slug + ".md";
        var md = tutorialMarkdown(title, template);
        var store = localStore();
        store[file] = {
          md: md,
          images: {},
          entries: [{ at: new Date().toISOString(), author: "You", note: "Created" }]
        };
        saveLocal(store);
        sessionStorage.setItem("wiki-open-edit", file);
        var next = "#/" + encodeURIComponent(file);
        if (location.hash === next) route();
        else location.hash = next;
      });
    });

    var imp = document.getElementById("import-tutorial");
    var fileInput = document.getElementById("import-file");
    if (imp && fileInput) {
      imp.addEventListener("click", function () { fileInput.click(); });
      fileInput.addEventListener("change", function () {
        var file = fileInput.files && fileInput.files[0];
        fileInput.value = "";
        if (!file) return;
        file.arrayBuffer().then(function (buf) { return unzip(new Uint8Array(buf)); }).then(function (entries) {
          var md = entries.filter(function (e) { return /\.md$/i.test(e.name); })[0];
          if (!md) throw new Error("That zip has no tutorial.");
          var text = new TextDecoder().decode(md.data);
          var title = "Imported tutorial";
          var m = text.match(/^title:\s*(.+)$/m);
          if (m) title = m[1].trim();
          var slug = title.toLowerCase().replace(/[^a-z0-9]+/g, "-").replace(/^-|-$/g, "") || "tutorial";
          var path = "My Tutorials/" + slug + ".md";
          var images = {};
          entries.forEach(function (e) {
            if (!/\.(png|jpg|jpeg|gif|webp)$/i.test(e.name)) return;
            var name = e.name.split("/").pop();
            var bin = "";
            for (var i = 0; i < e.data.length; i++) bin += String.fromCharCode(e.data[i]);
            images[name] = "data:image/" + (name.split(".").pop() || "png") + ";base64," + btoa(bin);
          });
          var store = localStore();
          store[path] = { md: text, images: images };
          saveLocal(store);
          location.hash = "#/" + encodeURIComponent(path);
          route();
        }).catch(function (err) {
          showError(escape(err && err.message ? err.message : String(err)));
        });
      });
    }
  }

  var btn = document.getElementById("nav-toggle");
  var side = document.querySelector(".sidebar");
  if (btn && side) {
    btn.addEventListener("click", function () {
      side.classList.toggle("open");
    });
  }

  mountCorner();
  window.addEventListener("hashchange", route);
  route();
})();
