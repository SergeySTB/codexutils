package com.aiusagemonitor.android;

final class WidgetIconSlots {
    static final int WIDTH_DP = 54;

    static int capacity(int widthDp) {
        return Math.max(1, widthDp / WIDTH_DP);
    }

    static int visible(int slots, int accounts) {
        if (accounts <= slots) return accounts;
        return slots == 1 ? 1 : slots - 1;
    }
}
