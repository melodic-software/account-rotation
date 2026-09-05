(function () {
  "use strict";

  var POLL_MS = 10000;
  var cards = document.getElementById("cards");
  var banner = document.getElementById("banner");
  var warnings = document.getElementById("warnings");
  var captured = document.getElementById("captured");
  var toast = document.getElementById("toast");
  var toastTimer = null;
  var switching = false;

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

  function render(dashboard) {
    captured.textContent = "as of " + new Date(dashboard.capturedAt).toLocaleTimeString();
    banner.hidden = !dashboard.banner;
    banner.textContent = dashboard.banner || "";
    warnings.innerHTML = "";
    warnings.hidden = dashboard.warnings.length === 0;
    dashboard.warnings.forEach(function (warning) { warnings.appendChild(element("li", null, warning)); });

    cards.innerHTML = "";
    dashboard.accounts.forEach(function (account) {
      var card = element("section", "card" + (account.isLive ? " live" : ""));
      card.appendChild(element("h2", null, account.email));
      var badges = element("div", "badges");
      if (account.isLive) { badges.appendChild(element("span", "badge live", "live")); }
      if (!account.hasCredentials && !account.isLive) { badges.appendChild(element("span", "badge needs-login", "needs login")); }
      card.appendChild(badges);
      var button = element("button", "switch", account.isLive ? "Live now" : "Switch");
      button.disabled = account.isLive || !account.hasCredentials || switching || !!dashboard.banner;
      button.addEventListener("click", function () { switchTo(account.email); });
      card.appendChild(button);
      cards.appendChild(card);
    });
  }

  function refresh() {
    return fetch("/api/dashboard", { headers: { "Accept": "application/json" } })
      .then(function (response) { return response.json(); })
      .then(render)
      .catch(function (error) { showToast("Dashboard unavailable: " + error, "error"); });
  }

  function switchTo(email) {
    switching = true;
    fetch("/api/accounts/" + encodeURIComponent(email) + "/switch", {
      method: "POST",
      headers: { "X-Account-Rotation": "1", "Accept": "application/json" }
    })
      .then(function (response) { return response.json().then(function (body) { return { ok: response.ok, body: body }; }); })
      .then(function (result) {
        if (result.ok) {
          var text = "Switched to " + result.body.now;
          if (result.body.parkedAs) { text += "; parked " + result.body.parkedAs; }
          if (result.body.identityMismatchWarning) { text += ". The CLI reports " + result.body.cliEmail + "; check /status."; }
          showToast(text, result.body.identityMismatchWarning ? "warn" : "ok");
        } else {
          showToast("Refused: " + (result.body.message || result.body.error || result.body.refusal), "error");
        }
      })
      .catch(function (error) { showToast("Switch failed: " + error, "error"); })
      .then(function () { switching = false; return refresh(); });
  }

  refresh();
  setInterval(refresh, POLL_MS);
})();
