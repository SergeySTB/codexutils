package com.aiusagemonitor.android;

final class WidgetIconSlots {
    static int diameter(int widthDp, int heightDp) {
        return Math.max(1, Math.min(widthDp - 6, heightDp - 6));
    }

    static int capacity(int widthDp, int diameterDp) {
        return Math.max(1, widthDp / (diameterDp + 6));
    }

    static int visible(int slots, int accounts) {
        if (accounts <= slots) return accounts;
        return slots == 1 ? 1 : slots - 1;
    }
}
