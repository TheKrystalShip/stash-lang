/**
 * copy-install.js
 * Wires up copy-to-clipboard for the install widget.
 * Buttons with data-copy-target="<id>" copy the text content of element#<id>.
 */
(function () {
    'use strict';

    document.addEventListener('DOMContentLoaded', function () {
        var buttons = document.querySelectorAll('.btn-copy[data-copy-target]');
        buttons.forEach(function (btn) {
            btn.addEventListener('click', function () {
                var targetId = btn.getAttribute('data-copy-target');
                var target = document.getElementById(targetId);
                if (!target) return;

                var text = target.textContent || '';
                if (navigator.clipboard && navigator.clipboard.writeText) {
                    navigator.clipboard.writeText(text).then(function () {
                        showCopied(btn);
                    }).catch(function () {
                        fallbackCopy(text, btn);
                    });
                } else {
                    fallbackCopy(text, btn);
                }
            });
        });
    });

    function showCopied(btn) {
        var original = btn.textContent;
        btn.textContent = 'Copied!';
        btn.disabled = true;
        setTimeout(function () {
            btn.textContent = original;
            btn.disabled = false;
        }, 2000);
    }

    function fallbackCopy(text, btn) {
        var textarea = document.createElement('textarea');
        textarea.value = text;
        textarea.style.position = 'fixed';
        textarea.style.opacity = '0';
        document.body.appendChild(textarea);
        textarea.select();
        try {
            document.execCommand('copy');
            showCopied(btn);
        } catch (e) {
            // Copy not supported in this environment.
        }
        document.body.removeChild(textarea);
    }
}());
