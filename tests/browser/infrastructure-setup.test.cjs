const { test } = require('node:test');
const assert = require('node:assert/strict');
const { readFileSync } = require('node:fs');
const { runInNewContext } = require('node:vm');
const source = readFileSync(new URL('../../web-components/src/Aetheric.Provisioning.Components/wwwroot/infrastructure-setup.js', `file://${__filename}`), 'utf8');

function page(count) {
    const element = () => ({ disabled: true, textContent: '', listeners: {}, addEventListener(name, fn) { this.listeners[name] = fn; } });
    const forms = Array.from({ length: count }, (_, i) => {
        const nodes = { fieldset: element(), '[role=status]': element(), '[data-test]': element(), '.reveal-password': element() };
        return Object.assign(element(), {
            dataset: { service: `service${i}` }, elements: { password: { value: 'test-only' } },
            querySelector: key => nodes[key], reportValidity: () => true,
        });
    });
    const finish = element(), progress = element(), status = element();
    const root = { dataset: {}, querySelectorAll: () => forms,
        querySelector: key => ({ '[data-finish]': finish, '[data-progress]': progress, '[data-save-result]': status })[key] };
    const events = {};
    const requests = [];
    class FormData {
        constructor(form) { this.values = new Map(form ? [['system', form.dataset.service], ['password', form.elements.password.value]] : []); }
        set(key, value) { this.values.set(key, value); }
        [Symbol.iterator]() { return this.values[Symbol.iterator](); }
    }
    const Blazor = { addEventListener: (name, fn) => { events[name] = fn; } };
    runInNewContext(source, { document: { querySelector: () => root }, window: { Blazor }, Blazor, FormData,
        setTimeout() {}, location: { assign() {} },
        fetch: async (url, options) => {
            requests.push({ url, data: options.body.values });
            return { status: 200, headers: { get: () => 'application/json' },
                json: async () => ({ success: true, receipt: `receipt-${options.body.values.get('system')}`, redirect: '/setup/complete' }) };
        }
    });
    return { forms, finish, progress, root, events, requests };
}

for (const count of [4, 6]) test(`${count} services unlock and require every connection before saving`, async () => {
    const p = page(count);
    for (const form of p.forms) assert.equal(form.querySelector('fieldset').disabled, false);
    assert.equal(p.progress.textContent, `0 of ${count} connections verified`);
    for (const form of p.forms) {
        assert.equal(p.finish.disabled, true);
        await form.listeners.submit({ preventDefault() {} });
    }
    assert.equal(p.finish.disabled, false);
    const first = p.forms[0];
    first.elements.password.value = 'changed';
    first.listeners.input();
    assert.equal(p.finish.disabled, true);
    await first.listeners.submit({ preventDefault() {} });
    await p.finish.listeners.click();
    const saved = p.requests.at(-1);
    assert.equal(saved.url, '/setup/infrastructure/save');
    for (const form of p.forms) assert.equal(saved.data.get(`${form.dataset.service}.receipt`), `receipt-${form.dataset.service}`);
});

test('an incomplete page is not permanently marked initialized', () => {
    const p = page(0);
    assert.equal(p.root.dataset.initialized, undefined);
});
