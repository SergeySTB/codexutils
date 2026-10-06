import XCTest

final class NavigationTests: XCTestCase {
    @MainActor
    func testEmptyStateNavigationAndCancelledLogin() {
        let app = XCUIApplication()
        app.launchArguments = ["-AppleLanguages", "(en)", "-AppleLocale", "en_US"]
        app.launch()
        XCTAssertTrue(app.tabBars.buttons["Overview"].waitForExistence(timeout: 10))
        app.tabBars.buttons["Widgets"].tap()
        XCTAssertTrue(app.navigationBars["Widgets"].exists)
        app.tabBars.buttons["Settings"].tap()
        XCTAssertTrue(app.navigationBars["Settings"].exists)
        app.tabBars.buttons["Overview"].tap()
        let add = app.navigationBars.buttons["Add account"]
        XCTAssertTrue(add.waitForExistence(timeout: 5))
        add.tap()
        let addClaude = app.descendants(matching: .any)["Add Claude"]
        XCTAssertTrue(addClaude.waitForExistence(timeout: 5), app.debugDescription)
        addClaude.tap()
        XCTAssertTrue(app.secureTextFields["OAuth token"].waitForExistence(timeout: 5))
        app.buttons["Cancel"].tap()
        XCTAssertTrue(app.navigationBars["Overview"].exists)
    }
}
