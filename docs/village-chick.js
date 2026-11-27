(function () {
  'use strict';
  var dialog = document.getElementById('my-chick');
  var status = document.getElementById('my-chick-status');
  var login = document.getElementById('my-chick-login');
  var start = document.getElementById('my-chick-start');
  var email = document.getElementById('my-chick-email');
  var code = document.getElementById('my-chick-code');
  var edit = document.getElementById('my-chick-edit');
  var look = edit.elements.mascot;
  var preview = document.getElementById('my-chick-preview');
  var assetBase = new URL('assets/mascot/', document.currentScript.src);
  var close = document.getElementById('my-chick-close');
  var busy = false;
  login.href = '/login?return=' + encodeURIComponent(location.pathname);

  async function request(action, method, payload) {
    var headers = { Accept: 'application/json' };
    if (payload) {
      headers['Content-Type'] = 'application/json';
      headers['X-Requested-With'] = 'chickxel';
    }
    var response = await fetch('/api/me/village' + action, { method: method, headers: headers, credentials: 'same-origin', body: payload && JSON.stringify(payload) });
    if (response.status === 401) { step(login); return null; }
    return response;
  }

  function step(control, message) {
    [login, start, email, code, edit].forEach(function (item) { item.hidden = item !== control; });
    status.textContent = message ? status.dataset[message] : '';
    if (dialog.open) (control ? control.querySelector('input, select') || control : close).focus();
  }

  function updatePreview() { preview.src = new URL('chick-' + look.value + '.png', assetBase).href; }
  function showChick(chick, message) {
    if (!chick) { step(start); return; }
    if (!chick.eligible) { step(null, 'ineligible'); return; }
    look.value = chick.mascot;
    edit.elements.crop.value = chick.crop;
    edit.elements.public.checked = chick.public;
    updatePreview();
    step(edit, message);
  }

  document.getElementById('my-chick-open').addEventListener('click', async function () {
    dialog.showModal();
    if (busy) return;
    busy = true;
    step(null, 'loading');
    try {
      var response = await request('');
      if (response && !response.ok) throw Error();
      if (response) showChick((await response.json()).chick);
    } catch (e) {
      step(null, 'error');
    } finally {
      busy = false;
    }
  });
  close.addEventListener('click', function () { dialog.close(); });
  dialog.addEventListener('close', function () { code.reset(); email.reset(); });
  dialog.addEventListener('keydown', function (event) {
    if (event.key === 'Enter' && event.target.matches('input:not([type=checkbox])')) event.preventDefault();
  });
  start.addEventListener('click', function () { step(email); });
  document.getElementById('my-chick-retry').addEventListener('click', function () { if (!busy) step(email); });
  look.addEventListener('change', updatePreview);

  function submit(form, action) {
    form.addEventListener('submit', async function (event) {
      event.preventDefault();
      if (busy) return;
      busy = true;
      var button = form.querySelector('button[type=submit]');
      button.disabled = true;
      try {
        await action();
      } catch (e) {
        status.textContent = status.dataset.error;
      } finally {
        button.disabled = false;
        busy = false;
      }
    });
  }
  function residentsChanged() { document.dispatchEvent(new Event('chickxel:residents-changed')); }
  submit(email, async function () {
    var response = await request('/code', 'POST', { email: email.elements.email.value, lang: document.documentElement.lang });
    if (!response) return;
    if (!response.ok) throw Error();
    code.reset();
    step(code, 'sent');
  });
  submit(code, async function () {
    var response = await request('/claim', 'POST', { code: code.elements.code.value });
    if (!response) return;
    if (response.status === 400) { step(code, 'badCode'); code.elements.code.select(); return; }
    if (!response.ok) throw Error();
    email.reset();
    code.reset();
    showChick((await response.json()).chick);
    residentsChanged();
  });
  submit(edit, async function () {
    var response = await request('', 'PUT', { mascot: look.value, crop: edit.elements.crop.value, public: edit.elements.public.checked });
    if (!response) return;
    if (response.status === 403) {
      var current = await request('');
      if (!current) return;
      if (current.ok && (await current.json()).chick?.eligible === false) { step(null, 'ineligible'); residentsChanged(); return; }
    }
    if (!response.ok) throw Error();
    showChick((await response.json()).chick, 'saved');
    residentsChanged();
  });
})();
