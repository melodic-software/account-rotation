(function () {
  "use strict";

  var POLL_MS = 10000;
  var BROWSERS = ["", "chrome", "edge", "brave"];
  var cards = document.getElementById("cards");
  var banner = document.getElementById("banner");
  var warnings = document.getElementById("warnings");
  var captured = document.getElementById("captured");
  var toast = document.getElementById("toast");
  var addForm = document.getElementById("add");
  var addEmail = document.getElementById("add-email");
  var toastTimer = null;
  var busy = false;

  function showToast(text, kind) {
    toast.textContent = text;
    toast.className = "toast " + (kind || "");
    toast.hidden = false;
    clearTimeout(toastTimer);
    toastTimer = setTimeout(function () { toast.hidden = true; }, 6000);
  }

  function element(tag, className, text) {
    var node = document.createElement(tag);
    if (className) { node.className = className; }
    if (text !== undefined) { node.textContent = text; }
    return node;
  }

  // The alias, browser, and profile-directory fields, built once and used both by
  // the Add form and by each card's Edit panel, so the two can never drift apart.
  function accountFields(container, entry) {
    var values = entry || {};
    var alias = element("input");
    alias.type = "text";
    alias.placeholder = "alias (optional)";
    alias.value = values.alias || "";

    var browser = element("select");
    BROWSERS.forEach(function (name) {
      var option = element("option", null, name === "" ? "no browser mapped" : name);
      option.value = name;
      browser.appendChild(option);
    });
    browser.value = values.browser || "";

    var profile = element("input");
    profile.type = "text";
    profile.placeholder = "browser profile directory, e.g. Profile 3";
    profile.value = values.browserProfileDirectory || "";

    [alias, browser, profile].forEach(function (field) { container.appendChild(field); });
    return function () {
      return {
        alias: alias.value.trim() || null,
        browser: browser.value || null,
        browserProfileDirectory: profile.value.trim() || null
      };
    };
  }

  function send(path, method, body) {
    var options = {
      method: method,
      headers: { "X-Claude-Code-Account-Rotation": "1", "Accept": "application/json" }
    };
    if (body) {
      options.headers["Content-Type"] = "application/json";
      options.body = JSON.stringify(body);
    }
    return fetch(path, options).then(function (response) {
      return response.json().then(function (payload) { return { ok: response.ok, body: payload }; });
    });
  }

  function refused(body) {
    return "Refused: " + (body.message || body.error || body.refusal || "unknown reason");
  }

  function mutate(path, method, body, onDone) {
    busy = true;
    setButtonsDisabled(true);
    return send(path, method, body)
      .then(function (result) {
        if (onDone) { return onDone(result); }
        if (!result.ok) { showToast(refused(result.body), "error"); }
        return null;
      })
      .catch(function (error) { showToast("Request failed: " + error, "error"); })
      .then(function () { busy = false; setButtonsDisabled(false); return refresh(); });
  }

  function accountPath(email, suffix) {
    return "/api/accounts/" + encodeURIComponent(email) + (suffix || "");
  }

  function switchTo(email) {
    return mutate(accountPath(email, "/switch"), "POST", null, function (result) {
      if (!result.ok) {
        showToast(refused(result.body), "error");
        return;
      }
      var text = "Switched to " + result.body.now;
      if (result.body.parkedAs) { text += "; parked " + result.body.parkedAs; }
      if (result.body.identityMismatchWarning) { text += ". The CLI reports " + result.body.cliEmail + "; check /status."; }
      showToast(text, result.body.identityMismatchWarning ? "warn" : "ok");
    });
  }

  function setPaused(email, paused) {
    return mutate(accountPath(email), "PATCH", { paused: paused }, function (result) {
      showToast(result.ok ? (paused ? email + " is out of the rotation" : email + " is back in the rotation") : refused(result.body), result.ok ? "ok" : "error");
    });
  }

  function adopt(email) {
    return mutate(accountPath(email, "/adopt-live"), "POST", null, function (result) {
      showToast(result.ok ? email + " is on the roster" : refused(result.body), result.ok ? "ok" : "error");
    });
  }

  function remove(email) {
    if (!window.confirm("Remove " + email + "? Its login is revoked and its profile folder is deleted.")) {
      return Promise.resolve();
    }
    return mutate(accountPath(email), "DELETE", null, function (result) {
      if (result.ok) {
        showToast(email + " removed" + (result.body.warning ? ". " + result.body.warning : " and logged out"), result.body.warning ? "warn" : "ok");
        return null;
      }
      // The delete is refused rather than stranding a token the tool believes it
      // revoked. Deleting anyway is the operator's call, and it is said plainly.
      if (result.body.refusal === "LogoutFailed" && window.confirm(result.body.message + "\n\nDelete the folder anyway, without revoking?")) {
        return send(accountPath(email) + "?logout=false", "DELETE", null).then(function (forced) {
          showToast(forced.ok ? email + " removed. " + forced.body.warning : refused(forced.body), forced.ok ? "warn" : "error");
        });
      }
      showToast(refused(result.body), "error");
      return null;
    });
  }

  function editPanel(account) {
    var panel = element("details", "edit");
    panel.appendChild(element("summary", null, "Edit"));
    var form = element("form", "roster-form");
    var read = accountFields(form, account.roster);
    var save = element("button", null, "Save");
    save.type = "submit";
    form.appendChild(save);
    form.addEventListener("submit", function (event) {
      event.preventDefault();
      mutate(accountPath(account.email), "PATCH", read(), function (result) {
        showToast(result.ok ? account.email + " updated" : refused(result.body), result.ok ? "ok" : "error");
      });
    });
    panel.appendChild(form);
    return panel;
  }

  function actionButton(label, className, onClick) {
    var button = element("button", className, label);
    button.type = "button";
    button.addEventListener("click", onClick);
    return button;
  }

  function render(dashboard) {
    captured.textContent = "as of " + new Date(dashboard.capturedAt).toLocaleTimeString();
    banner.hidden = !dashboard.banner;
    banner.textContent = dashboard.banner || "";
    warnings.innerHTML = "";
    warnings.hidden = dashboard.warnings.length === 0;
    dashboard.warnings.forEach(function (warning) { warnings.appendChild(element("li", null, warning)); });

    cards.innerHTML = "";
    dashboard.accounts.forEach(function (account) {
      var roster = account.roster;
      var paused = !!(roster && roster.paused);
      var card = element("section", "card" + (account.isLive ? " live" : "") + (paused ? " paused" : ""));
      card.appendChild(element("h2", null, (roster && roster.alias) ? roster.alias + " (" + account.email + ")" : account.email));

      var badges = element("div", "badges");
      if (account.isLive) { badges.appendChild(element("span", "badge live", "live")); }
      if (paused) { badges.appendChild(element("span", "badge paused", "paused")); }
      if (!account.hasCredentials && !account.isLive) { badges.appendChild(element("span", "badge needs-login", "needs login")); }
      if (!roster) { badges.appendChild(element("span", "badge off-roster", "not on roster")); }
      card.appendChild(badges);

      if (roster && roster.browser) {
        card.appendChild(element("p", "muted", roster.browser + (roster.browserProfileDirectory ? " / " + roster.browserProfileDirectory : "")));
      }

      var actions = element("div", "actions");
      var switchButton = actionButton(account.isLive ? "Live now" : "Switch", "switch", function () { switchTo(account.email); });
      // A paused account is out of the ranked queue, not off the page: the operator
      // can still switch to it by hand.
      switchButton.disabled = account.isLive || !account.hasCredentials || busy || !!dashboard.banner;
      actions.appendChild(switchButton);

      if (roster) {
        actions.appendChild(actionButton(paused ? "Resume" : "Pause", "secondary", function () { setPaused(account.email, !paused); }));
      } else if (account.isLive) {
        actions.appendChild(actionButton("Adopt", "secondary", function () { adopt(account.email); }));
      }

      if (!account.isLive) {
        actions.appendChild(actionButton("Remove", "danger", function () { remove(account.email); }));
      }

      card.appendChild(actions);
      if (roster) { card.appendChild(editPanel(account)); }
      cards.appendChild(card);
    });
  }

  function refresh() {
    return fetch("/api/dashboard", { headers: { "Accept": "application/json" } })
      .then(function (response) { return response.json(); })
      .then(render)
      .catch(function (error) { showToast("Dashboard unavailable: " + error, "error"); });
  }

  function setButtonsDisabled(disabled) {
    // Synchronously, at click time: a second click during the lock wait would
    // otherwise send a second request whose refusal toast overwrote the outcome of
    // the first. The re-enable is unconditional and the following render applies
    // the per-account state, so a failed request can never leave a button dead.
    Array.prototype.forEach.call(document.querySelectorAll("button"), function (button) { button.disabled = disabled; });
  }

  var readAddFields = accountFields(document.getElementById("add-fields"), null);
  addForm.addEventListener("submit", function (event) {
    event.preventDefault();
    var body = readAddFields();
    body.email = addEmail.value.trim();
    mutate("/api/accounts", "POST", body, function (result) {
      if (result.ok) {
        addForm.reset();
        showToast(body.email + " added; it needs a login before it can be switched to", "ok");
      } else {
        showToast(refused(result.body), "error");
      }
    });
  });

  refresh();
  setInterval(refresh, POLL_MS);
})();
